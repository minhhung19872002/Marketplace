using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;
using StackExchange.Redis;

namespace ShopHub.Infrastructure.Notifications;

public interface ISmsSender
{
    Task SendAsync(string to, string text, CancellationToken ct);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string html, CancellationToken ct);
}

/// <summary>Writes the SMS to sys.simulated_sms instead of a carrier — same contract, viewable for demos/tests.</summary>
public sealed class SimulatedSmsSender(ShopHubDbContext db, IClock clock) : ISmsSender
{
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        db.SimulatedSms.Add(new SimulatedSms(to, text, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// eSMS brandname SMS (G3) — on with SH_SMS_PROVIDER=esms. eSMS answers HTTP 200 with CodeResult "100" when it accepted
/// the message; anything else throws so the outbox retries the message.
/// </summary>
public sealed class EsmsSmsSender(IHttpClientFactory http, EsmsOptions options, Microsoft.Extensions.Logging.ILogger<EsmsSmsSender> logger) : ISmsSender
{
    public const string HttpClientName = "esms";
    // eSMS reads the field names as written (ApiKey, Phone…), not camelCase
    private static readonly JsonSerializerOptions Exact = new() { PropertyNamingPolicy = null };

    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.CreateClient(HttpClientName).PostAsJsonAsync(options.Endpoint, new
        {
            ApiKey = options.ApiKey, SecretKey = options.SecretKey, Phone = to, Content = text, Brandname = options.Brandname, SmsType = options.SmsType,
        }, Exact, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var code = body.TryGetProperty("CodeResult", out var c) ? c.ToString() : null;
        if (code != "100")
        {
            // Never the phone number or the text (OTP) in the log
            var reason = body.TryGetProperty("ErrorMessage", out var m) ? m.GetString() : null;
            throw new InvalidOperationException($"eSMS từ chối tin nhắn (CodeResult {code ?? "?"}): {reason}");
        }
        logger.LogDebug("eSMS accepted SMS {SmsId}", body.TryGetProperty("SMSID", out var id) ? id.ToString() : "?");
    }
}

public sealed class SmtpEmailSender(ShopHubSettings settings) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.SmtpFrom));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, SecureSocketOptions.Auto, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

public sealed class SmsOutboxHandler(ISmsSender sender) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.NotifySms;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var sms = JsonSerializer.Deserialize<SmsPayload>(payload, Json) ?? throw new InvalidOperationException("Nội dung SMS rỗng.");
        return sender.SendAsync(sms.To, sms.Text, ct);
    }
}

public sealed class EmailOutboxHandler(IEmailSender sender) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => OutboxTypes.NotifyEmail;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var mail = JsonSerializer.Deserialize<EmailPayload>(payload, Json) ?? throw new InvalidOperationException("Nội dung email rỗng.");
        return sender.SendAsync(mail.To, mail.Subject, mail.Html, ct);
    }
}

// Tell every instance to drop cached session state for this user/session
public sealed class SessionsChangedHandler(IConnectionMultiplexer redis) : IOutboxHandler
{
    public static readonly RedisChannel Channel = RedisChannel.Literal("sh:iam:sessions-changed");

    public string Type => OutboxTypes.SessionsChanged;

    public async Task HandleAsync(string payload, CancellationToken ct) =>
        await redis.GetSubscriber().PublishAsync(Channel, payload);
}

public sealed class SessionsChangedSubscriber(IConnectionMultiplexer redis, ISessionValidator validator)
    : Microsoft.Extensions.Hosting.IHostedService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetSubscriber().SubscribeAsync(SessionsChangedHandler.Channel, (_, value) =>
            {
                if (value.IsNullOrEmpty) return;
                var data = JsonSerializer.Deserialize<SessionsChangedPayload>(value.ToString(), Json);
                if (data is null) return;
                if (data.SessionId is { } sid) validator.InvalidateSession(sid);
                else validator.InvalidateUser(data.UserId);
            });
        }
        catch (RedisException)
        {
            // Redis down: cached entries still expire after 30 s
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Wakes an in-process dispatcher right after commit, so an OTP SMS goes out in milliseconds rather than at the
/// next Hangfire poll (15 s) or cron tick (1 min). The recurring job stays as the safety net; parallel dispatchers
/// on several instances never collide thanks to FOR UPDATE SKIP LOCKED.
/// </summary>
public sealed class ImmediateOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ShopHubSettings settings,
    ILogger<ImmediateOutboxDispatcher> logger) : BackgroundService, IOutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Kick()
    {
        // With background jobs disabled (integration tests) the dispatcher is invoked explicitly by the caller
        if (!settings.JobsEnabled) return;
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The cron job will retry; never let the loop die
                logger.LogWarning(ex, "Immediate outbox dispatch failed");
            }
        }
    }
}

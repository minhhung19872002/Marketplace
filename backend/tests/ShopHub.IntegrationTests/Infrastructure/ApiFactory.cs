using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Domain.Iam;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ShopHub.IntegrationTests.Infrastructure;

/// <summary>
/// Real PostgreSQL 16 + Redis 7 in containers, the real API pipeline (auth, middleware, EF interceptors) on top.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtSecret = "integration-test-secret-key-at-least-32-chars!";
    public const string DefaultPassword = "Matkhau123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("shophub")
        .WithUsername("shophub")
        .WithPassword("shophub-test")
        .Build();

    private readonly IContainer _redis = new ContainerBuilder()
        .WithImage("redis:7-alpine")
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();

    private readonly IContainer _minio = new ContainerBuilder()
        .WithImage("minio/minio:RELEASE.2025-04-22T22-12-26Z")
        .WithCommand("server", "/data")
        .WithEnvironment("MINIO_ROOT_USER", "shophub")
        .WithEnvironment("MINIO_ROOT_PASSWORD", "shophub-test-secret")
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/minio/health/ready")))
        .Build();

    private readonly IContainer _meili = new ContainerBuilder()
        .WithImage("getmeili/meilisearch:v1.11")
        .WithEnvironment("MEILI_MASTER_KEY", "integration-test-meili-key")
        .WithEnvironment("MEILI_NO_ANALYTICS", "true")
        .WithPortBinding(7700, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(7700).ForPath("/health")))
        .Build();

    private static int _phoneSeq = Random.Shared.Next(1_000_000, 9_000_000);

    /// <summary>Mails "sent" by the API during the tests (no SMTP server in the suite).</summary>
    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Stands in for pg_dump (not installed on the test machine); the real one runs in the API image.</summary>
    public FakeDumper Dumper { get; } = new();

    public string BackupDirectory { get; } = Path.Combine(Path.GetTempPath(), $"shophub-it-backups-{Guid.NewGuid():N}");

    /// <summary>VNPay / MoMo / ZaloPay / GHN / GHTK sandboxes, in process.</summary>
    public FakeProviders Providers { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ShopHub.Infrastructure.Ops.IDatabaseDumper>();
            services.AddSingleton<ShopHub.Infrastructure.Ops.IDatabaseDumper>(Dumper);
            services.RemoveAll<ShopHub.Infrastructure.Notifications.IEmailSender>();
            services.AddSingleton<ShopHub.Infrastructure.Notifications.IEmailSender>(Emails);
            foreach (var name in new[] { "vnpay", "momo", "zalopay", "ghn", "ghtk" })
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new FakeProvidersHandler(Providers));
            // Image links of Excel imports: https://img.test/… serves a PNG, https://big.test/… is over the size limit
            services.AddHttpClient(ShopHub.Infrastructure.Media.RemoteImageFetcher.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeImageHostHandler());
            services.AddHttpClient(ShopHub.Infrastructure.Identity.GoogleTokenVerifier.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeGoogleCertsHandler());
        });

    public string ConnectionString => _postgres.GetConnectionString();
    public string RedisEndpoint => $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}";
    public string MinioEndpoint => $"127.0.0.1:{_minio.GetMappedPublicPort(9000)}";
    public string MediaBaseUrl => $"http://{MinioEndpoint}";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _minio.StartAsync(), _meili.StartAsync());

        // Program reads SH_* before the host is built, so they are provided as environment variables
        Environment.SetEnvironmentVariable("SH_DB_CONNECTION", ConnectionString);
        Environment.SetEnvironmentVariable("SH_REDIS_URL", RedisEndpoint);
        Environment.SetEnvironmentVariable("SH_JWT_SECRET", JwtSecret);
        Environment.SetEnvironmentVariable("SH_JOBS_ENABLED", "false");
        Environment.SetEnvironmentVariable("SH_GOOGLE_CLIENT_ID", FakeGoogle.ClientId);
        Environment.SetEnvironmentVariable("SH_GOOGLE_CERTS_URL", FakeGoogle.CertsUrl);
        Environment.SetEnvironmentVariable("SH_SEED_SAMPLE", "false");
        Environment.SetEnvironmentVariable("SH_MINIO_ENDPOINT", MinioEndpoint);
        Environment.SetEnvironmentVariable("SH_MINIO_ACCESS_KEY", "shophub");
        Environment.SetEnvironmentVariable("SH_MINIO_SECRET_KEY", "shophub-test-secret");
        // In tests the browser-facing base is MinIO itself (no gateway)
        Environment.SetEnvironmentVariable("SH_MEDIA_PUBLIC_URL", MediaBaseUrl);
        Environment.SetEnvironmentVariable("SH_DATA_KEY", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("SH_MEILI_URL", $"http://127.0.0.1:{_meili.GetMappedPublicPort(7700)}");
        Environment.SetEnvironmentVariable("SH_MEILI_MASTER_KEY", "integration-test-meili-key");
        Environment.SetEnvironmentVariable("SH_LOG_DIR", Path.Combine(Path.GetTempPath(), "shophub-it-logs"));
        Environment.SetEnvironmentVariable("SH_BACKUP_DIR", BackupDirectory);

        // The whole suite signs in from one IP
        Environment.SetEnvironmentVariable("SH_RATE_LIMIT_AUTH", "100000");
        Environment.SetEnvironmentVariable("SH_PAYMENT_SIMULATED", "true");
        // Every test request shares one (unknown) client IP: the per-IP ceiling would trip across the whole suite
        Environment.SetEnvironmentVariable("SH_RATE_LIMIT_GLOBAL", "1000000");
        Environment.SetEnvironmentVariable("SH_RATE_LIMIT_OTP", "100000");
        // Per phone / e-mail: low enough for RateLimitTests to reach, higher than any other test's sign-ins for one account
        Environment.SetEnvironmentVariable("SH_RATE_LIMIT_IDENTIFIER", "30");
        Environment.SetEnvironmentVariable("SH_RATE_LIMIT_SEARCH", "100000");
        // Tests read what they just wrote: no output cache
        Environment.SetEnvironmentVariable("SH_OUTPUT_CACHE_SECONDS", "0");

        // Real gateways / carriers switched on with test keys, answered by FakeProviders
        Environment.SetEnvironmentVariable("SH_VNPAY_TMN_CODE", FakeProviders.VnPayTmn);
        Environment.SetEnvironmentVariable("SH_VNPAY_HASH_SECRET", FakeProviders.VnPaySecret);
        Environment.SetEnvironmentVariable("SH_VNPAY_API_URL", $"https://{FakeProviders.VnPayHost}/merchant_webapi/api/transaction");
        Environment.SetEnvironmentVariable("SH_MOMO_PARTNER_CODE", FakeProviders.MoMoPartner);
        Environment.SetEnvironmentVariable("SH_MOMO_ACCESS_KEY", FakeProviders.MoMoAccess);
        Environment.SetEnvironmentVariable("SH_MOMO_SECRET_KEY", FakeProviders.MoMoSecret);
        Environment.SetEnvironmentVariable("SH_MOMO_ENDPOINT", $"https://{FakeProviders.MoMoHost}");
        Environment.SetEnvironmentVariable("SH_ZALOPAY_APP_ID", FakeProviders.ZaloPayAppId);
        Environment.SetEnvironmentVariable("SH_ZALOPAY_KEY1", FakeProviders.ZaloPayKey1);
        Environment.SetEnvironmentVariable("SH_ZALOPAY_KEY2", FakeProviders.ZaloPayKey2);
        Environment.SetEnvironmentVariable("SH_ZALOPAY_ENDPOINT", $"https://{FakeProviders.ZaloPayHost}");
        Environment.SetEnvironmentVariable("SH_ZALOPAY_INSTALLMENT_METHOD", FakeProviders.ZaloPayInstallment);
        Environment.SetEnvironmentVariable("SH_GHN_TOKEN", FakeProviders.GhnToken);
        Environment.SetEnvironmentVariable("SH_GHN_SHOP_ID", FakeProviders.GhnShopId.ToString());
        Environment.SetEnvironmentVariable("SH_GHN_ENDPOINT", $"https://{FakeProviders.GhnHost}/shiip/public-api");
        Environment.SetEnvironmentVariable("SH_GHN_WEBHOOK_TOKEN", FakeProviders.GhnWebhookToken);
        Environment.SetEnvironmentVariable("SH_GHTK_TOKEN", FakeProviders.GhtkToken);
        Environment.SetEnvironmentVariable("SH_GHTK_ENDPOINT", $"https://{FakeProviders.GhtkHost}");
        Environment.SetEnvironmentVariable("SH_GHTK_WEBHOOK_TOKEN", FakeProviders.GhtkWebhookToken);
        Environment.SetEnvironmentVariable("SH_CALLBACK_BASE_URL", "https://callback.shophub.test");
        Providers.Divisions = parent => WithDbAsync<IReadOnlyList<(string, string)>>(async db =>
            (await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == parent).Select(d => new { d.Code, d.Name }).ToListAsync())
            .Select(d => (d.Code, d.Name)).ToList());

        // Force host start (migrations + seed) before tests run
        _ = Server;

        // The real carriers' channels exist (keys are set) but stay off, so every other test keeps the simulated
        // channels only; ProviderTests switch them on one at a time
        foreach (var code in new[] { "GHN_STD", "GHTK_STD" }) await SetCarrierActiveAsync(code, false);
    }

    public Task SetCarrierActiveAsync(string code, bool active) =>
        WithDbAsync(async db =>
        {
            var c = await db.Carriers.SingleAsync(x => x.Code == code);
            c.Configure(c.Name, c.Description, active, c.SupportsCod, c.SameProvinceOnly, c.DaysSameProvince, c.DaysSameRegion, c.DaysCrossRegion);
            await db.SaveChangesAsync();
        });

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask(),
            _meili.DisposeAsync().AsTask());
    }

    /// <summary>Unique, valid-looking Vietnamese mobile number per call.</summary>
    public static string NewPhone() => $"09{Interlocked.Increment(ref _phoneSeq):D8}";

    public HttpClient Authorized(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Create a user directly in the DB (optionally with a role holding the given permissions) and sign in via the API.</summary>
    public async Task<TestUser> CreateUserAsync(params string[] permissions)
    {
        var phone = NewPhone();
        Guid userId;
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopHubDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = User.Register(phone, null, hasher.Hash(DefaultPassword), $"Người thử {phone[^4..]}", DateTimeOffset.UtcNow);
            db.Users.Add(user);
            if (permissions.Length > 0)
            {
                var role = new Role($"T_{Guid.NewGuid():N}"[..20].ToUpperInvariant(), "Vai trò thử", "", isSystem: false);
                role.SetPermissions(permissions);
                db.Roles.Add(role);
                db.UserRoles.Add(new UserRole(user.Id, role.Id));
            }
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var login = await LoginAsync(phone, DefaultPassword);
        return new TestUser(userId, phone, login.AccessToken, login.RefreshToken, Authorized(login.AccessToken));
    }

    public Task<HttpClient> ClientWithPermissionsAsync(params string[] permissions) =>
        CreateUserAsync(permissions).ContinueWith(t => t.Result.Client);

    public async Task<LoginData> LoginAsync(string identifier, string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new { identifier, password });
        response.EnsureSuccessStatusCode();
        return (await response.ReadEnvelopeAsync<LoginData>()).Data!;
    }

    /// <summary>Deliver pending outbox messages (background jobs are disabled in tests).</summary>
    public async Task<OutboxDispatchResult> DispatchOutboxAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(CancellationToken.None);
    }

    /// <summary>Read the newest OTP texted to a phone by the simulated SMS provider.</summary>
    public async Task<string> LatestOtpAsync(string phone)
    {
        await DispatchOutboxAsync();
        var text = await WithDbAsync(db => db.SimulatedSms.Where(s => s.To == phone)
            .OrderByDescending(s => s.CreatedAt).Select(s => s.Content).FirstAsync());
        return System.Text.RegularExpressions.Regex.Match(text, @"\b\d{6}\b").Value;
    }

    /// <summary>Full phone + OTP registration through the public API.</summary>
    public async Task<LoginData> RegisterAsync(string phone, string password = DefaultPassword, string fullName = "Khách Hàng Thử")
    {
        var client = CreateClient();
        (await client.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" })).EnsureSuccessStatusCode();
        var code = await LatestOtpAsync(ShopHub.Application.Identity.Identifiers.NormalisePhone(phone) ?? phone);
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { target = phone, purpose = "Register", code });
        verify.EnsureSuccessStatusCode();
        var ticket = (await verify.ReadEnvelopeAsync()).Data.GetProperty("ticket").GetString();
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { target = phone, ticket, password, fullName, acceptTerms = true });
        register.EnsureSuccessStatusCode();
        return (await register.ReadEnvelopeAsync<LoginData>()).Data!;
    }

    // Hand-signed tokens are only used to prove that forged/expired tokens are refused
    public static string IssueToken(Guid userId, IEnumerable<string> permissions, TimeSpan? lifetime = null)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()), new("sid", Guid.NewGuid().ToString()) };
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        var token = new JwtSecurityToken(
            issuer: "ShopHub",
            audience: "ShopHub",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task WithDbAsync(Func<ShopHubDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ShopHubDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<ShopHubDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ShopHubDbContext>());
    }
}

public record LoginUser(Guid Id, string FullName, string? Phone, string? Email, bool MustChangePassword);

public record LoginData(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, LoginUser User);

public record TestUser(Guid Id, string Phone, string AccessToken, string RefreshToken, HttpClient Client);

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

internal static class JsonElementExtensions
{
    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;
}

public sealed class RecordingEmailSender : ShopHub.Infrastructure.Notifications.IEmailSender
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string To, string Subject, string Html)> _sent = new();

    public IReadOnlyList<(string To, string Subject, string Html)> Sent => _sent.ToList();

    public Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        _sent.Enqueue((to, subject, html));
        return Task.CompletedTask;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Commerce.Providers;

/// <summary>
/// ZaloPay gateway, API v2 (sb-openapi.zalopay.vn by default). Create an order with a form POST signed by key1 over
/// "app_id|app_trans_id|app_user|amount|app_time|embed_data|item" → order_url; the callback POSTs {data, mac, type} with
/// mac = HMAC-SHA256(key2, data) and is answered {return_code, return_message}; query and refund are signed by key1.
/// app_trans_id = yyMMdd (Vietnam date) + "_" + the payment id, so the callback finds the payment again.
/// </summary>
public sealed class ZaloPayGateway(
    IHttpClientFactory http,
    ZaloPayOptions options,
    ProviderSettings providers,
    ISystemParameters parameters,
    IClock clock,
    ILogger<ZaloPayGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "zalopay";
    public const string HttpClientName = "zalopay";
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
    // ZaloPay's field names are snake_case
    private static readonly JsonSerializerOptions Snake = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public PaymentMethod Method => PaymentMethod.ZaloPay;
    public string Provider => ProviderName;
    public string DisplayName => "ZaloPay";

    public IReadOnlyList<PaymentOption> Options =>
    [
        PaymentOption.Default, PaymentOption.DomesticCard, PaymentOption.InternationalCard, PaymentOption.QrCode,
        .. options.InstallmentMethod is null ? Array.Empty<PaymentOption>() : [PaymentOption.Installment],
        .. options.PayLaterMethod is null ? Array.Empty<PaymentOption>() : [PaymentOption.PayLater],
    ];

    /// <summary>The documented "preferred_payment_method" values; instalment / pay-later codes come from the merchant contract.</summary>
    public IReadOnlyList<string> PreferredMethods(PaymentOption option) => option switch
    {
        PaymentOption.DomesticCard => ["domestic_card", "account"],
        PaymentOption.InternationalCard => ["international_card"],
        PaymentOption.QrCode => ["vietqr"],
        PaymentOption.Installment => [options.InstallmentMethod ?? throw new GatewayUnavailableException("ZaloPay chưa bật trả góp cho cửa hàng.")],
        PaymentOption.PayLater => [options.PayLaterMethod ?? throw new GatewayUnavailableException("ZaloPay chưa bật mua trước trả sau cho cửa hàng.")],
        _ => [],
    };

    public static string TransId(Guid paymentId, DateTimeOffset at) =>
        $"{at.ToOffset(Vietnam).ToString("yyMMdd", CultureInfo.InvariantCulture)}_{paymentId:N}";

    public static Guid? PaymentIdOf(string? appTransId) =>
        appTransId is { Length: > 7 } && appTransId[6] == '_' && Guid.TryParseExact(appTransId[7..], "N", out var id) ? id : null;

    public async Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct)
    {
        var site = (await parameters.GetStringAsync(ParameterKeys.SitePublicUrl, ct)).TrimEnd('/');
        var now = request.CreatedAt ?? clock.UtcNow;
        var appTransId = TransId(request.PaymentId, now);
        var appTime = now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var embed = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["redirecturl"] = site + request.ReturnPath,
            ["preferred_payment_method"] = PreferredMethods(request.Option),
        });
        const string item = "[]";
        const string appUser = "ShopHub";
        var amount = request.Amount.ToString(CultureInfo.InvariantCulture);
        var expire = Math.Clamp((long)(request.ExpiresAt - now).TotalSeconds, 300, 2_592_000);
        var fields = new Dictionary<string, string>
        {
            ["app_id"] = options.AppId,
            ["app_user"] = appUser,
            ["app_trans_id"] = appTransId,
            ["app_time"] = appTime,
            ["amount"] = amount,
            ["item"] = item,
            ["embed_data"] = embed,
            ["description"] = ProviderText.Ascii(request.Description),
            ["bank_code"] = "",
            ["callback_url"] = $"{providers.CallbackBaseUrl ?? site}/api/payments/webhooks/{ProviderName}",
            ["expire_duration_seconds"] = expire.ToString(CultureInfo.InvariantCulture),
            ["mac"] = Sign(options.Key1, $"{options.AppId}|{appTransId}|{appUser}|{amount}|{appTime}|{embed}|{item}"),
        };
        var reply = await PostAsync("/v2/create", fields, ct);
        if (reply is null) throw new GatewayUnavailableException("Không kết nối được ZaloPay, vui lòng thử lại hoặc chọn phương thức khác.");
        if (reply.ReturnCode != 1 || string.IsNullOrEmpty(reply.OrderUrl))
            throw new GatewayUnavailableException($"ZaloPay từ chối tạo giao dịch: {reply.SubReturnMessage ?? reply.ReturnMessage ?? "lỗi không xác định"}.");
        return new GatewayPaymentStart(reply.OrderUrl);
    }

    public sealed record CallbackBody(string Data, string Mac, int Type);

    public sealed record CallbackData(long AppId, string AppTransId, long AppTime, string? AppUser, long Amount, string? EmbedData, string? Item,
        long ZpTransId, long ServerTime, int Channel, string? MerchantUserId, long UserFeeAmount, long DiscountAmount);

    /// <summary>ZaloPay only calls back for payments that went through; the mac is HMAC-SHA256(key2, data).</summary>
    public GatewayCallback? VerifyCallback(InboundWebhook webhook)
    {
        CallbackBody? body;
        CallbackData? data;
        try
        {
            body = JsonSerializer.Deserialize<CallbackBody>(webhook.Body, Json);
            if (body?.Data is null || body.Mac is null) return null;
            if (!ProviderText.SameHex(body.Mac, Sign(options.Key2, body.Data))) return null;
            data = JsonSerializer.Deserialize<CallbackData>(body.Data, Snake);
        }
        catch (JsonException)
        {
            return null;
        }
        if (data is null || data.AppId.ToString(CultureInfo.InvariantCulture) != options.AppId) return null;
        if (PaymentIdOf(data.AppTransId) is not { } paymentId) return null;
        return new GatewayCallback($"{data.AppTransId}:{data.ZpTransId}", paymentId, data.ZpTransId.ToString(CultureInfo.InvariantCulture), data.Amount,
            true, null, webhook.Body);
    }

    public async Task<GatewayQueryResult> QueryAsync(Payment payment, CancellationToken ct)
    {
        var appTransId = TransId(payment.Id, payment.CreatedAt);
        var reply = await PostAsync("/v2/query", new Dictionary<string, string>
        {
            ["app_id"] = options.AppId,
            ["app_trans_id"] = appTransId,
            ["mac"] = Sign(options.Key1, $"{options.AppId}|{appTransId}|{options.Key1}"),
        }, ct);
        if (reply is null) return new GatewayQueryResult(GatewayTxnStatus.Pending, null, payment.Amount, "{}");
        // 1 = paid, 3 = not paid yet / being processed, 2 = failed
        var status = reply.ReturnCode switch
        {
            1 => GatewayTxnStatus.Succeeded,
            3 => GatewayTxnStatus.Pending,
            _ => GatewayTxnStatus.Failed,
        };
        return new GatewayQueryResult(status, reply.ZpTransId > 0 ? reply.ZpTransId.ToString(CultureInfo.InvariantCulture) : null,
            reply.Amount ?? payment.Amount, JsonSerializer.Serialize(reply, Json));
    }

    public async Task<bool> RefundAsync(Payment payment, long amount, string reason, CancellationToken ct)
    {
        if (payment.ProviderTxnId is null) return false;
        var timestamp = clock.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        // m_refund_id: yyMMdd_appid_unique (one per refund)
        var refundId = $"{clock.UtcNow.ToOffset(Vietnam).ToString("yyMMdd", CultureInfo.InvariantCulture)}_{options.AppId}_{RandomNumberGenerator.GetHexString(16, true)}";
        var description = ProviderText.Ascii(reason);
        var value = amount.ToString(CultureInfo.InvariantCulture);
        var reply = await PostAsync("/v2/refund", new Dictionary<string, string>
        {
            ["app_id"] = options.AppId,
            ["m_refund_id"] = refundId,
            ["zp_trans_id"] = payment.ProviderTxnId,
            ["amount"] = value,
            ["timestamp"] = timestamp,
            ["description"] = description,
            ["mac"] = Sign(options.Key1, $"{options.AppId}|{payment.ProviderTxnId}|{value}|{description}|{timestamp}"),
        }, ct);
        // 1 = refunded, 3 = accepted and being processed by the bank; 2 / anything else = refused
        var ok = reply is { ReturnCode: 1 or 3 };
        if (!ok) logger.LogWarning("ZaloPay refund of payment {PaymentId} refused: {Code}", payment.Id, reply?.ReturnCode);
        return ok;
    }

    public string Acknowledge(bool accepted) => Acknowledge(accepted, accepted ? "PAID" : "INVALID");

    /// <summary>return_code 1 = taken, 2 = already taken (ZaloPay stops retrying), -1 = refused (bad mac / data).</summary>
    public string Acknowledge(bool accepted, string result) => JsonSerializer.Serialize(!accepted
        ? new { return_code = -1, return_message = "mac not equal" }
        : result == Application.Features.Payments.WebhookResults.Duplicate
            ? new { return_code = 2, return_message = "duplicate" }
            : new { return_code = 1, return_message = "success" });

    // ZaloPay reads the JSON body, not the status
    public int AcknowledgeStatus(bool accepted) => 200;

    private static string Sign(string key, string data) => ProviderText.HmacSha256Hex(key, data);

    public sealed record Reply(int ReturnCode, string? ReturnMessage, int? SubReturnCode, string? SubReturnMessage, string? OrderUrl,
        string? ZpTransToken, long ZpTransId, long? Amount, bool? IsProcessing);

    private async Task<Reply?> PostAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.CreateClient(HttpClientName).PostAsync(options.Endpoint.TrimEnd('/') + path, content, ct);
            return JsonSerializer.Deserialize<Reply>(await response.Content.ReadAsStringAsync(ct), Snake);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ZaloPay call {Path} failed", path);
            return null;
        }
    }
}

using System.Net.Http.Json;
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
/// MoMo all-in-one gateway, API v2 (test-payment.momo.vn by default): create → payUrl, IPN POSTed as signed JSON
/// (HMAC-SHA256 over "accessKey=…&amp;amount=…" in the documented field order) answered with HTTP 204, query and refund.
/// </summary>
public sealed class MoMoGateway(
    IHttpClientFactory http,
    MoMoOptions options,
    ProviderSettings providers,
    ISystemParameters parameters,
    ILogger<MoMoGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "momo";
    public const string HttpClientName = "momo";
    private const string RequestType = "captureWallet";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public PaymentMethod Method => PaymentMethod.MoMo;
    public string Provider => ProviderName;
    public string DisplayName => "Ví MoMo";

    public async Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct)
    {
        var site = (await parameters.GetStringAsync(ParameterKeys.SitePublicUrl, ct)).TrimEnd('/');
        var ipnUrl = $"{providers.CallbackBaseUrl ?? site}/api/payments/webhooks/{ProviderName}";
        var orderId = request.PaymentId.ToString("N");
        var requestId = orderId;
        var info = ProviderText.Ascii(request.Description);
        var redirect = site + request.ReturnPath;
        const string extra = "";
        var signature = Sign($"accessKey={options.AccessKey}&amount={request.Amount}&extraData={extra}&ipnUrl={ipnUrl}&orderId={orderId}" +
                             $"&orderInfo={info}&partnerCode={options.PartnerCode}&redirectUrl={redirect}&requestId={requestId}&requestType={RequestType}");
        var reply = await PostAsync("/v2/gateway/api/create", new
        {
            partnerCode = options.PartnerCode, requestId, amount = request.Amount, orderId, orderInfo = info, redirectUrl = redirect, ipnUrl,
            requestType = RequestType, extraData = extra, lang = "vi", autoCapture = true, signature,
        }, ct);
        if (reply is null) throw new GatewayUnavailableException("Không kết nối được MoMo, vui lòng thử lại hoặc chọn phương thức khác.");
        if (reply.ResultCode != 0 || string.IsNullOrEmpty(reply.PayUrl))
            throw new GatewayUnavailableException($"MoMo từ chối tạo giao dịch: {reply.Message ?? "lỗi không xác định"}.");
        return new GatewayPaymentStart(reply.PayUrl);
    }

    public sealed record Ipn(string PartnerCode, string OrderId, string RequestId, long Amount, string? OrderInfo, string? OrderType, long TransId,
        int ResultCode, string? Message, string? PayType, long ResponseTime, string? ExtraData, string Signature);

    public static string IpnSignatureData(Ipn n, string accessKey) =>
        $"accessKey={accessKey}&amount={n.Amount}&extraData={n.ExtraData}&message={n.Message}&orderId={n.OrderId}&orderInfo={n.OrderInfo}" +
        $"&orderType={n.OrderType}&partnerCode={n.PartnerCode}&payType={n.PayType}&requestId={n.RequestId}&responseTime={n.ResponseTime}" +
        $"&resultCode={n.ResultCode}&transId={n.TransId}";

    public GatewayCallback? VerifyCallback(InboundWebhook webhook)
    {
        Ipn? n;
        try
        {
            n = JsonSerializer.Deserialize<Ipn>(webhook.Body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (n is null || n.PartnerCode != options.PartnerCode) return null;
        if (!ProviderText.SameHex(n.Signature, Sign(IpnSignatureData(n, options.AccessKey)))) return null;
        if (!Guid.TryParseExact(n.OrderId, "N", out var paymentId)) return null;
        var success = n.ResultCode == 0;
        return new GatewayCallback($"{n.OrderId}:{n.TransId}:{n.ResultCode}", paymentId, n.TransId > 0 ? n.TransId.ToString() : null, n.Amount, success,
            success ? null : $"MoMo: {n.Message} (mã {n.ResultCode}).", webhook.Body);
    }

    public async Task<GatewayQueryResult> QueryAsync(Payment payment, CancellationToken ct)
    {
        var orderId = payment.Id.ToString("N");
        var requestId = RandomNumberGenerator.GetHexString(32, lowercase: true);
        var signature = Sign($"accessKey={options.AccessKey}&orderId={orderId}&partnerCode={options.PartnerCode}&requestId={requestId}");
        var reply = await PostAsync("/v2/gateway/api/query", new { partnerCode = options.PartnerCode, requestId, orderId, lang = "vi", signature }, ct);
        if (reply is null) return new GatewayQueryResult(GatewayTxnStatus.Pending, null, payment.Amount, "{}");
        var status = reply.ResultCode switch
        {
            0 => GatewayTxnStatus.Succeeded,
            // 1000 = waiting for the user, 7000 / 7002 = being processed, 9000 = authorised, not yet captured
            1000 or 7000 or 7002 or 9000 => GatewayTxnStatus.Pending,
            _ => GatewayTxnStatus.Failed,
        };
        return new GatewayQueryResult(status, reply.TransId > 0 ? reply.TransId.ToString() : null, reply.Amount ?? payment.Amount,
            JsonSerializer.Serialize(reply, Json));
    }

    public async Task<bool> RefundAsync(Payment payment, long amount, string reason, CancellationToken ct)
    {
        if (payment.ProviderTxnId is null || !long.TryParse(payment.ProviderTxnId, out var transId)) return false;
        // Each refund is its own MoMo order
        var orderId = RandomNumberGenerator.GetHexString(32, lowercase: true);
        var requestId = orderId;
        var description = ProviderText.Ascii(reason);
        var signature = Sign($"accessKey={options.AccessKey}&amount={amount}&description={description}&orderId={orderId}" +
                             $"&partnerCode={options.PartnerCode}&requestId={requestId}&transId={transId}");
        var reply = await PostAsync("/v2/gateway/api/refund",
            new { partnerCode = options.PartnerCode, orderId, requestId, amount, transId, lang = "vi", description, signature }, ct);
        var ok = reply is { ResultCode: 0 };
        if (!ok) logger.LogWarning("MoMo refund of payment {PaymentId} refused: {Code}", payment.Id, reply?.ResultCode);
        return ok;
    }

    public string Acknowledge(bool accepted) => "";

    // MoMo expects 204 No Content once the IPN is taken
    public int AcknowledgeStatus(bool accepted) => accepted ? 204 : 400;

    private string Sign(string data) => ProviderText.HmacSha256Hex(options.SecretKey, data);

    private sealed record Reply(int ResultCode, string? Message, string? PayUrl, long TransId, long? Amount);

    private async Task<Reply?> PostAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient(HttpClientName).PostAsJsonAsync(options.Endpoint.TrimEnd('/') + path, body, Json, ct);
            // MoMo answers business errors with 4xx and the same JSON body
            return await response.Content.ReadFromJsonAsync<Reply>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "MoMo call {Path} failed", path);
            return null;
        }
    }
}

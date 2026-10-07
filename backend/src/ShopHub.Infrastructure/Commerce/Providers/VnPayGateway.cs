using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Payments;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Commerce.Providers;

/// <summary>
/// VNPay payment gateway, API 2.1.0 (sandbox by default). Pay: redirect to the signed vpcpay URL (HMAC-SHA512 over the
/// sorted, URL-encoded fields). IPN: GET with the same signed query string; reply {"RspCode","Message"}. Query and refund
/// go to merchant_webapi with the pipe-joined signature of the documentation.
/// </summary>
public sealed class VnPayGateway(
    IHttpClientFactory http,
    VnPayOptions options,
    ISystemParameters parameters,
    IClock clock,
    ILogger<VnPayGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "vnpay";
    public const string HttpClientName = "vnpay";
    private const string Version = "2.1.0";

    public PaymentMethod Method => PaymentMethod.VnPay;
    public string Provider => ProviderName;
    public string DisplayName => "VNPay (thẻ ATM / Visa / QR ngân hàng)";

    public async Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct)
    {
        var site = (await parameters.GetStringAsync(ParameterKeys.SitePublicUrl, ct)).TrimEnd('/');
        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = Version,
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = (request.Amount * 100).ToString(),
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = request.PaymentId.ToString("N"),
            ["vnp_OrderInfo"] = ProviderText.Ascii(request.Description),
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn",
            ["vnp_ReturnUrl"] = site + request.ReturnPath,
            ["vnp_IpAddr"] = request.ClientIp ?? "127.0.0.1",
            ["vnp_CreateDate"] = ProviderText.VnStamp(request.CreatedAt ?? clock.UtcNow),
            ["vnp_ExpireDate"] = ProviderText.VnStamp(request.ExpiresAt),
        };
        if (BankCode(request.Option) is { } bank) fields["vnp_BankCode"] = bank;
        var query = ProviderText.VnPayCanonical(fields);
        return new GatewayPaymentStart($"{options.PayUrl}?{query}&vnp_SecureHash={ProviderText.HmacSha512Hex(options.HashSecret, query)}");
    }

    public IReadOnlyList<PaymentOption> Options => [PaymentOption.Default, PaymentOption.QrCode, PaymentOption.DomesticCard, PaymentOption.InternationalCard];

    /// <summary>vnp_BankCode: VNPAYQR (QR), VNBANK (thẻ / tài khoản nội địa), INTCARD (thẻ quốc tế); none = VNPay's own choice page.</summary>
    public static string? BankCode(PaymentOption option) => option switch
    {
        PaymentOption.QrCode => "VNPAYQR",
        PaymentOption.DomesticCard => "VNBANK",
        PaymentOption.InternationalCard => "INTCARD",
        PaymentOption.Default => null,
        _ => throw new GatewayUnavailableException("VNPay không hỗ trợ hình thức thanh toán này."),
    };

    public GatewayCallback? VerifyCallback(InboundWebhook webhook)
    {
        var fields = webhook.Query.Where(q => q.Key.StartsWith("vnp_", StringComparison.Ordinal)).ToDictionary(q => q.Key, q => q.Value);
        if (!fields.Remove("vnp_SecureHash", out var hash)) return null;
        fields.Remove("vnp_SecureHashType");
        if (!ProviderText.SameHex(hash, ProviderText.HmacSha512Hex(options.HashSecret, ProviderText.VnPayCanonical(fields)))) return null;
        if (fields.GetValueOrDefault("vnp_TmnCode") != options.TmnCode) return null;
        if (!Guid.TryParseExact(fields.GetValueOrDefault("vnp_TxnRef"), "N", out var paymentId)) return null;
        if (!long.TryParse(fields.GetValueOrDefault("vnp_Amount"), out var amount100)) return null;

        var response = fields.GetValueOrDefault("vnp_ResponseCode");
        var status = fields.GetValueOrDefault("vnp_TransactionStatus");
        var txn = fields.GetValueOrDefault("vnp_TransactionNo");
        var success = response == "00" && status == "00";
        var raw = JsonSerializer.Serialize(fields);
        return new GatewayCallback($"{paymentId:N}:{txn}:{response}:{status}", paymentId, string.IsNullOrEmpty(txn) || txn == "0" ? null : txn,
            amount100 / 100, success, success ? null : $"VNPay từ chối giao dịch (mã {response}).", raw);
    }

    public async Task<GatewayQueryResult> QueryAsync(Payment payment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var requestId = RandomNumberGenerator.GetHexString(32, lowercase: true);
        var txnRef = payment.Id.ToString("N");
        var transactionDate = ProviderText.VnStamp(payment.CreatedAt);
        var createDate = ProviderText.VnStamp(now);
        const string info = "Truy van giao dich";
        const string ip = "127.0.0.1";
        var hash = ProviderText.HmacSha512Hex(options.HashSecret,
            string.Join('|', requestId, Version, "querydr", options.TmnCode, txnRef, transactionDate, createDate, ip, info));
        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId, ["vnp_Version"] = Version, ["vnp_Command"] = "querydr", ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_TxnRef"] = txnRef, ["vnp_OrderInfo"] = info, ["vnp_TransactionDate"] = transactionDate, ["vnp_CreateDate"] = createDate,
            ["vnp_IpAddr"] = ip, ["vnp_SecureHash"] = hash,
        };
        var reply = await PostAsync(body, ct);
        if (reply is null || !ResponseSigned(reply) || reply.GetValueOrDefault("vnp_ResponseCode") != "00")
            return new GatewayQueryResult(GatewayTxnStatus.Pending, null, payment.Amount, reply is null ? "{}" : JsonSerializer.Serialize(reply));
        var status = reply.GetValueOrDefault("vnp_TransactionStatus") switch
        {
            "00" => GatewayTxnStatus.Succeeded,
            "01" => GatewayTxnStatus.Pending,
            _ => GatewayTxnStatus.Failed,
        };
        var amount = long.TryParse(reply.GetValueOrDefault("vnp_Amount"), out var a) ? a / 100 : payment.Amount;
        return new GatewayQueryResult(status, reply.GetValueOrDefault("vnp_TransactionNo"), amount, JsonSerializer.Serialize(reply));
    }

    public async Task<bool> RefundAsync(Payment payment, long amount, string reason, CancellationToken ct)
    {
        if (payment.ProviderTxnId is null) return false;
        var requestId = RandomNumberGenerator.GetHexString(32, lowercase: true);
        var type = amount >= payment.Amount ? "02" : "03";
        var txnRef = payment.Id.ToString("N");
        var amount100 = (amount * 100).ToString();
        var transactionDate = ProviderText.VnStamp(payment.CreatedAt);
        var createDate = ProviderText.VnStamp(clock.UtcNow);
        const string createdBy = "shophub";
        const string ip = "127.0.0.1";
        var info = ProviderText.Ascii(reason);
        var hash = ProviderText.HmacSha512Hex(options.HashSecret, string.Join('|', requestId, Version, "refund", options.TmnCode, type, txnRef,
            amount100, payment.ProviderTxnId, transactionDate, createdBy, createDate, ip, info));
        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId, ["vnp_Version"] = Version, ["vnp_Command"] = "refund", ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_TransactionType"] = type, ["vnp_TxnRef"] = txnRef, ["vnp_Amount"] = amount100, ["vnp_OrderInfo"] = info,
            ["vnp_TransactionNo"] = payment.ProviderTxnId, ["vnp_TransactionDate"] = transactionDate, ["vnp_CreateBy"] = createdBy,
            ["vnp_CreateDate"] = createDate, ["vnp_IpAddr"] = ip, ["vnp_SecureHash"] = hash,
        };
        var reply = await PostAsync(body, ct);
        var ok = reply is not null && ResponseSigned(reply) && reply.GetValueOrDefault("vnp_ResponseCode") == "00";
        if (!ok) logger.LogWarning("VNPay refund of payment {PaymentId} refused: {Code}", payment.Id, reply?.GetValueOrDefault("vnp_ResponseCode"));
        return ok;
    }

    public string Acknowledge(bool accepted) => Acknowledge(accepted, WebhookResults.Paid);

    public string Acknowledge(bool accepted, string result) => !accepted ? Reply("97", "Invalid Checksum") : result switch
    {
        WebhookResults.UnknownPayment => Reply("01", "Order not Found"),
        WebhookResults.Duplicate => Reply("02", "Order already confirmed"),
        WebhookResults.AmountMismatch => Reply("04", "invalid amount"),
        _ => Reply("00", "Confirm Success"),
    };

    // VNPay reads the RspCode, always over HTTP 200
    public int AcknowledgeStatus(bool accepted) => 200;

    private static string Reply(string code, string message) => JsonSerializer.Serialize(new { RspCode = code, Message = message });

    /// <summary>Responses of merchant_webapi are signed over their fields in documentation order.</summary>
    private bool ResponseSigned(IReadOnlyDictionary<string, string> r)
    {
        string V(string k) => r.GetValueOrDefault(k) ?? "";
        var command = V("vnp_Command");
        var data = command == "refund"
            ? string.Join('|', V("vnp_ResponseId"), command, V("vnp_ResponseCode"), V("vnp_Message"), V("vnp_TmnCode"), V("vnp_TxnRef"), V("vnp_Amount"),
                V("vnp_BankCode"), V("vnp_PayDate"), V("vnp_TransactionNo"), V("vnp_TransactionType"), V("vnp_TransactionStatus"), V("vnp_OrderInfo"))
            : string.Join('|', V("vnp_ResponseId"), command, V("vnp_ResponseCode"), V("vnp_Message"), V("vnp_TmnCode"), V("vnp_TxnRef"), V("vnp_Amount"),
                V("vnp_BankCode"), V("vnp_PayDate"), V("vnp_TransactionNo"), V("vnp_TransactionType"), V("vnp_TransactionStatus"), V("vnp_OrderInfo"),
                V("vnp_PromotionCode"), V("vnp_PromotionAmount"));
        var ok = ProviderText.SameHex(r.GetValueOrDefault("vnp_SecureHash"), ProviderText.HmacSha512Hex(options.HashSecret, data));
        if (!ok) logger.LogWarning("VNPay {Command} response with a bad signature ignored", command);
        return ok;
    }

    private async Task<Dictionary<string, string>?> PostAsync(Dictionary<string, string> body, CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient(HttpClientName).PostAsJsonAsync(options.ApiUrl, body, ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(ct);
            return json?.ToDictionary(p => p.Key, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "VNPay {Command} call failed", body.GetValueOrDefault("vnp_Command"));
            return null;
        }
    }
}

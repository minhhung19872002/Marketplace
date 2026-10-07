using Microsoft.Extensions.Configuration;

namespace ShopHub.Infrastructure.Configuration;

/// <summary>
/// Real payment gateways and carriers (spec IV, V, Phase 11). Each one is switched on only when its keys are set in
/// .env; without keys the simulated gateway / carrier keep the whole flow working. Endpoints default to the sandboxes.
/// </summary>
public sealed class ProviderSettings
{
    public VnPayOptions? VnPay { get; init; }
    public MoMoOptions? MoMo { get; init; }
    public ZaloPayOptions? ZaloPay { get; init; }
    public GhnOptions? Ghn { get; init; }
    public GhtkOptions? Ghtk { get; init; }
    // Push (G2): Firebase Cloud Messaging HTTP v1 — on when the service-account key is set
    public FcmOptions? Fcm { get; init; }
    // SMS (G3): eSMS brandname — on when SH_SMS_PROVIDER=esms and its keys are set
    public EsmsOptions? Esms { get; init; }

    // Public base URL the providers call back (IPN / webhooks), e.g. https://shophub.example.vn; null = SITE.PUBLIC_URL
    public string? CallbackBaseUrl { get; init; }

    public static ProviderSettings FromConfiguration(IConfiguration c)
    {
        static string? Value(IConfiguration c, string key) => string.IsNullOrWhiteSpace(c[key]) ? null : c[key]!.Trim();

        return new ProviderSettings
        {
            VnPay = Value(c, "SH_VNPAY_TMN_CODE") is { } tmn && Value(c, "SH_VNPAY_HASH_SECRET") is { } secret
                ? new VnPayOptions(tmn, secret,
                    Value(c, "SH_VNPAY_PAY_URL") ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                    Value(c, "SH_VNPAY_API_URL") ?? "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction")
                : null,
            MoMo = Value(c, "SH_MOMO_PARTNER_CODE") is { } partner && Value(c, "SH_MOMO_ACCESS_KEY") is { } access
                   && Value(c, "SH_MOMO_SECRET_KEY") is { } momoSecret
                ? new MoMoOptions(partner, access, momoSecret, Value(c, "SH_MOMO_ENDPOINT") ?? "https://test-payment.momo.vn")
                : null,
            ZaloPay = Value(c, "SH_ZALOPAY_APP_ID") is { } appId && Value(c, "SH_ZALOPAY_KEY1") is { } key1 && Value(c, "SH_ZALOPAY_KEY2") is { } key2
                ? new ZaloPayOptions(appId, key1, key2, Value(c, "SH_ZALOPAY_ENDPOINT") ?? "https://sb-openapi.zalopay.vn",
                    Value(c, "SH_ZALOPAY_INSTALLMENT_METHOD"), Value(c, "SH_ZALOPAY_PAYLATER_METHOD"))
                : null,
            Ghn = Value(c, "SH_GHN_TOKEN") is { } ghnToken && int.TryParse(Value(c, "SH_GHN_SHOP_ID"), out var shopId)
                ? new GhnOptions(ghnToken, shopId, Value(c, "SH_GHN_ENDPOINT") ?? "https://dev-online-gateway.ghn.vn/shiip/public-api",
                    Value(c, "SH_GHN_WEBHOOK_TOKEN") ?? throw new InvalidOperationException("Bật GHN cần SH_GHN_WEBHOOK_TOKEN (chuỗi bí mật trong URL webhook)."))
                : null,
            Ghtk = Value(c, "SH_GHTK_TOKEN") is { } ghtkToken
                ? new GhtkOptions(ghtkToken, Value(c, "SH_GHTK_CLIENT_SOURCE"), Value(c, "SH_GHTK_ENDPOINT") ?? "https://services-staging.ghtklab.com",
                    Value(c, "SH_GHTK_WEBHOOK_TOKEN") ?? throw new InvalidOperationException("Bật GHTK cần SH_GHTK_WEBHOOK_TOKEN (chuỗi bí mật trong URL webhook)."))
                : null,
            CallbackBaseUrl = Value(c, "SH_CALLBACK_BASE_URL")?.TrimEnd('/'),
            Fcm = Value(c, "SH_FCM_SERVICE_ACCOUNT") is { } account ? FcmOptions.Parse(account, Value(c, "SH_FCM_API_BASE")) : null,
            Esms = Value(c, "SH_ESMS_API_KEY") is { } esmsKey && Value(c, "SH_ESMS_SECRET_KEY") is { } esmsSecret
                ? new EsmsOptions(esmsKey, esmsSecret,
                    Value(c, "SH_ESMS_BRANDNAME") ?? throw new InvalidOperationException("Bật eSMS cần SH_ESMS_BRANDNAME (tên thương hiệu đã đăng ký với eSMS)."),
                    Value(c, "SH_ESMS_ENDPOINT") ?? "https://rest.esms.vn/MainService.svc/json/SendMultipleMessage_V4_post_json/",
                    Value(c, "SH_ESMS_SMS_TYPE") ?? "2")
                : null,
        };
    }
}

public sealed record VnPayOptions(string TmnCode, string HashSecret, string PayUrl, string ApiUrl);

public sealed record MoMoOptions(string PartnerCode, string AccessKey, string SecretKey, string Endpoint);

// InstallmentMethod / PayLaterMethod: the "preferred_payment_method" codes ZaloPay gives the merchant for trả góp / mua trước trả sau
// (only in the contract, not public) — set → those ways are offered at checkout
public sealed record ZaloPayOptions(string AppId, string Key1, string Key2, string Endpoint, string? InstallmentMethod = null, string? PayLaterMethod = null);

public sealed record GhnOptions(string Token, int ShopId, string Endpoint, string WebhookToken);

// ClientSource: the partner code GHTK gives (X-Client-Source header), optional on older accounts
public sealed record GhtkOptions(string Token, string? ClientSource, string Endpoint, string WebhookToken);

/// <summary>
/// The Firebase service-account key (the JSON Google gives, raw, base64 or a file path) and where FCM lives —
/// <see cref="ApiBase"/> / <see cref="TokenUri"/> point at a fake server in tests.
/// </summary>
public sealed record FcmOptions(string ProjectId, string ClientEmail, string PrivateKey, string TokenUri, string ApiBase)
{
    public static FcmOptions Parse(string value, string? apiBase)
    {
        var json = value.TrimStart().StartsWith('{') ? value
            : File.Exists(value) ? File.ReadAllText(value)
            : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Field(string name) => root.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } s
            ? s : throw new InvalidOperationException($"SH_FCM_SERVICE_ACCOUNT thiếu trường \"{name}\".");
        return new FcmOptions(Field("project_id"), Field("client_email"), Field("private_key"),
            root.TryGetProperty("token_uri", out var t) && t.GetString() is { Length: > 0 } uri ? uri : "https://oauth2.googleapis.com/token",
            apiBase ?? "https://fcm.googleapis.com");
    }
}

// SmsType "2" = brandname customer-care messages (OTP, order notices); the brandname must be registered with eSMS
public sealed record EsmsOptions(string ApiKey, string SecretKey, string Brandname, string Endpoint, string SmsType);

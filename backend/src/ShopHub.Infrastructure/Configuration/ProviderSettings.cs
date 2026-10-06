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
    public GhnOptions? Ghn { get; init; }
    public GhtkOptions? Ghtk { get; init; }

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
            Ghn = Value(c, "SH_GHN_TOKEN") is { } ghnToken && int.TryParse(Value(c, "SH_GHN_SHOP_ID"), out var shopId)
                ? new GhnOptions(ghnToken, shopId, Value(c, "SH_GHN_ENDPOINT") ?? "https://dev-online-gateway.ghn.vn/shiip/public-api",
                    Value(c, "SH_GHN_WEBHOOK_TOKEN") ?? throw new InvalidOperationException("Bật GHN cần SH_GHN_WEBHOOK_TOKEN (chuỗi bí mật trong URL webhook)."))
                : null,
            Ghtk = Value(c, "SH_GHTK_TOKEN") is { } ghtkToken
                ? new GhtkOptions(ghtkToken, Value(c, "SH_GHTK_CLIENT_SOURCE"), Value(c, "SH_GHTK_ENDPOINT") ?? "https://services-staging.ghtklab.com",
                    Value(c, "SH_GHTK_WEBHOOK_TOKEN") ?? throw new InvalidOperationException("Bật GHTK cần SH_GHTK_WEBHOOK_TOKEN (chuỗi bí mật trong URL webhook)."))
                : null,
            CallbackBaseUrl = Value(c, "SH_CALLBACK_BASE_URL")?.TrimEnd('/'),
        };
    }
}

public sealed record VnPayOptions(string TmnCode, string HashSecret, string PayUrl, string ApiUrl);

public sealed record MoMoOptions(string PartnerCode, string AccessKey, string SecretKey, string Endpoint);

public sealed record GhnOptions(string Token, int ShopId, string Endpoint, string WebhookToken);

// ClientSource: the partner code GHTK gives (X-Client-Source header), optional on older accounts
public sealed record GhtkOptions(string Token, string? ClientSource, string Endpoint, string WebhookToken);

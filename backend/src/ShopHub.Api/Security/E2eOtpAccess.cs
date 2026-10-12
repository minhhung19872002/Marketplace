using System.Security.Cryptography;
using System.Text;

namespace ShopHub.Api.Security;

/// <summary>
/// Who may read the simulated SMS inbox (<c>/api/dev/sms</c>, decision #224). In Development with simulated SMS: anyone
/// (local stack, integration tests). Anywhere else only the e2e suite on a demo site: simulated SMS, SITE.MODE=demo, the
/// SH_E2E_OTP_KEY secret (its own key — not an admin password) presented in the X-E2E-Key header, and a number in the
/// e2e range (099…), never another buyer's code. Every other case answers 404.
/// </summary>
public static class E2eOtpAccess
{
    public const string Header = "X-E2E-Key";
    // Numbers the e2e suite registers with: 099 + 7 digits
    public const string PhonePrefix = "099";
    // A short key could be guessed through the endpoint
    public const int MinKeyLength = 24;

    public static bool Allows(bool development, string smsProvider, bool demo, string? configuredKey, string? presentedKey, string? phone)
    {
        if (smsProvider != "simulated") return false;
        if (development) return true;
        if (!demo || configuredKey is null || configuredKey.Length < MinKeyLength || string.IsNullOrEmpty(presentedKey)) return false;
        if (phone is null || !phone.StartsWith(PhonePrefix, StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configuredKey), Encoding.UTF8.GetBytes(presentedKey));
    }
}

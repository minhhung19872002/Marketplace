using FluentAssertions;
using ShopHub.Api.Security;
using Xunit;

namespace ShopHub.UnitTests;

// G8 B (decision #224): the e2e suite reads its own OTPs on a demo site, nobody else ever does
public class E2eOtpAccessTests
{
    private const string Key = "e2e-otp-key-0123456789abcdef";
    private const string E2ePhone = "0991234567";

    [Fact]
    public void Development_with_simulated_sms_is_open_as_before() =>
        E2eOtpAccess.Allows(true, "simulated", demo: false, null, null, "0912345678").Should().BeTrue();

    [Fact]
    public void A_demo_site_answers_the_e2e_key_for_e2e_numbers() =>
        E2eOtpAccess.Allows(false, "simulated", demo: true, Key, Key, E2ePhone).Should().BeTrue();

    [Theory]
    [InlineData("live mode", false, Key, Key, E2ePhone, "simulated")]
    [InlineData("no key configured", true, null, Key, E2ePhone, "simulated")]
    [InlineData("short key configured", true, "short-key", "short-key", E2ePhone, "simulated")]
    [InlineData("no key sent", true, Key, null, E2ePhone, "simulated")]
    [InlineData("wrong key", true, Key, "e2e-otp-key-0123456789abcdeX", E2ePhone, "simulated")]
    [InlineData("a buyer's number", true, Key, Key, "0912345678", "simulated")]
    [InlineData("real SMS provider", true, Key, Key, E2ePhone, "esms")]
    public void Everything_else_is_closed(string because, bool demo, string? configured, string? presented, string phone, string provider) =>
        E2eOtpAccess.Allows(false, provider, demo, configured, presented, phone).Should().BeFalse(because);

    [Fact]
    public void Development_with_a_real_sms_provider_is_closed() =>
        E2eOtpAccess.Allows(true, "esms", demo: true, Key, Key, E2ePhone).Should().BeFalse();
}

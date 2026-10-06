using FluentAssertions;
using ShopHub.Application.Identity;

namespace ShopHub.UnitTests.Application;

public class IdentifiersTests
{
    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("+84 912 345 678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("0912.345.678", "0912345678")]
    [InlineData("0312345678", "0312345678")]
    [InlineData("0212345678", null)]   // landline prefix
    [InlineData("091234567", null)]    // too short
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void NormalisePhone_accepts_vietnamese_mobiles_only(string raw, string? expected)
    {
        Identifiers.NormalisePhone(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("  An.Nguyen@Example.COM ", "an.nguyen@example.com")]
    [InlineData("khong-phai-email", null)]
    public void NormaliseEmail_trims_and_lowercases(string raw, string? expected)
    {
        Identifiers.NormaliseEmail(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("0912345678", IdentifierKind.Phone)]
    [InlineData("a@b.vn", IdentifierKind.Email)]
    [InlineData("admin", IdentifierKind.Username)]
    public void Classify_detects_the_identifier_kind(string raw, IdentifierKind kind)
    {
        Identifiers.Classify(raw)!.Value.Kind.Should().Be(kind);
    }

    [Fact]
    public void Classify_rejects_garbage()
    {
        Identifiers.Classify("a b").Should().BeNull();
    }

    [Fact]
    public void MaskPhone_hides_the_middle()
    {
        Identifiers.MaskPhone("0912345678").Should().Be("0912***678");
    }
}

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Logistics;
using ShopHub.Infrastructure.Commerce.Providers;
using ShopHub.Infrastructure.Configuration;
using Xunit;

namespace ShopHub.UnitTests;

/// <summary>Phase 11: the documented signing rules of VNPay / MoMo and the status / place-name mapping of GHN / GHTK.</summary>
public class ProviderRulesTests
{
    private const string VnPaySecret = "UNITTESTSECRET";

    private static VnPayGateway VnPay() => new(null!, new VnPayOptions("TMN01", VnPaySecret, "https://pay.test", "https://api.test"), null!, null!, null!);

    private static string Hmac512(string data) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(VnPaySecret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    [Fact]
    public void Vnpay_canonical_string_sorts_keys_ordinally_and_url_encodes_with_plus_for_spaces()
    {
        var canonical = ProviderText.VnPayCanonical(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "abc", ["vnp_Amount"] = "1000000", ["vnp_OrderInfo"] = "Thanh toan don SH1", ["vnp_ReturnUrl"] = "http://x.vn/a?b=1",
            ["vnp_BankCode"] = "",
        });
        canonical.Should().Be("vnp_Amount=1000000&vnp_OrderInfo=Thanh+toan+don+SH1&vnp_ReturnUrl=http%3A%2F%2Fx.vn%2Fa%3Fb%3D1&vnp_TxnRef=abc",
            "rỗng thì bỏ, khoá theo thứ tự byte, giá trị mã hoá URL");
    }

    private static InboundWebhook Ipn(Dictionary<string, string> fields, string? hash = null) =>
        new(new Dictionary<string, string>(),
            new Dictionary<string, string>(fields) { ["vnp_SecureHash"] = hash ?? Hmac512(ProviderText.VnPayCanonical(fields)), ["vnp_SecureHashType"] = "HmacSHA512" }, "");

    [Fact]
    public void Vnpay_ipn_is_accepted_only_with_its_signature_and_merchant_code()
    {
        var paymentId = Guid.NewGuid();
        var fields = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "15000000", ["vnp_ResponseCode"] = "00", ["vnp_TransactionStatus"] = "00", ["vnp_TmnCode"] = "TMN01",
            ["vnp_TransactionNo"] = "14123456", ["vnp_TxnRef"] = paymentId.ToString("N"), ["vnp_OrderInfo"] = "Thanh toan",
        };
        var ok = VnPay().VerifyCallback(Ipn(fields));
        ok.Should().NotBeNull();
        ok!.PaymentId.Should().Be(paymentId);
        ok.Amount.Should().Be(150_000);
        ok.Success.Should().BeTrue();
        ok.ProviderTxnId.Should().Be("14123456");

        VnPay().VerifyCallback(Ipn(fields, hash: new string('a', 128))).Should().BeNull("chữ ký sai");
        var other = new Dictionary<string, string>(fields) { ["vnp_TmnCode"] = "KHAC" };
        VnPay().VerifyCallback(Ipn(other)).Should().BeNull("mã website khác");
        var failed = new Dictionary<string, string>(fields) { ["vnp_ResponseCode"] = "24", ["vnp_TransactionStatus"] = "02" };
        VnPay().VerifyCallback(Ipn(failed))!.Success.Should().BeFalse("khách huỷ giao dịch (mã 24)");
    }

    [Fact]
    public void Vnpay_acknowledges_in_its_own_codes()
    {
        VnPay().Acknowledge(false, "").Should().Contain("\"RspCode\":\"97\"");
        VnPay().Acknowledge(true, "DUPLICATE").Should().Contain("\"RspCode\":\"02\"");
        VnPay().Acknowledge(true, "AMOUNT_MISMATCH").Should().Contain("\"RspCode\":\"04\"");
        VnPay().Acknowledge(true, "UNKNOWN_PAYMENT").Should().Contain("\"RspCode\":\"01\"");
        VnPay().Acknowledge(true, "PAID").Should().Contain("\"RspCode\":\"00\"");
    }

    [Fact]
    public void Momo_ipn_signature_covers_the_documented_fields_in_alphabetical_order()
    {
        var n = new MoMoGateway.Ipn("MOMO", "o1", "r1", 50_000, "Nap tien", "momo_wallet", 123, 0, "Thành công.", "qr", 1700, "", "sig");
        MoMoGateway.IpnSignatureData(n, "AK").Should().Be(
            "accessKey=AK&amount=50000&extraData=&message=Thành công.&orderId=o1&orderInfo=Nap tien&orderType=momo_wallet&partnerCode=MOMO" +
            "&payType=qr&requestId=r1&responseTime=1700&resultCode=0&transId=123");
    }

    [Theory]
    [InlineData("picked", ShipmentStatus.Picked)]
    [InlineData("transporting", ShipmentStatus.InTransit)]
    [InlineData("delivering", ShipmentStatus.OutForDelivery)]
    [InlineData("delivered", ShipmentStatus.Delivered)]
    [InlineData("delivery_fail", ShipmentStatus.Failed)]
    [InlineData("waiting_to_return", ShipmentStatus.Returning)]
    [InlineData("returned", ShipmentStatus.Returned)]
    [InlineData("cancel", ShipmentStatus.Cancelled)]
    [InlineData("ready_to_pick", ShipmentStatus.Created)]
    public void Ghn_statuses_map_to_shipment_statuses(string ghn, ShipmentStatus expected) => GhnCarrier.Map(ghn).Should().Be(expected);

    [Theory]
    [InlineData(-1, ShipmentStatus.Cancelled)]
    [InlineData(3, ShipmentStatus.Picked)]
    [InlineData(4, ShipmentStatus.OutForDelivery)]
    [InlineData(5, ShipmentStatus.Delivered)]
    [InlineData(9, ShipmentStatus.Failed)]
    [InlineData(20, ShipmentStatus.Returning)]
    [InlineData(21, ShipmentStatus.Returned)]
    [InlineData(2, ShipmentStatus.Created)]
    public void Ghtk_statuses_map_to_shipment_statuses(int ghtk, ShipmentStatus expected) => GhtkCarrier.Map(ghtk).Should().Be(expected);

    [Theory]
    [InlineData("Thành phố Hồ Chí Minh", "hochiminh")]
    [InlineData("TP. Hồ Chí Minh", "hochiminh")]
    [InlineData("Tỉnh Bà Rịa - Vũng Tàu", "bariavungtau")]
    [InlineData("Quận 01", "1")]
    [InlineData("Quận 1", "1")]
    [InlineData("Huyện Củ Chi", "cuchi")]
    [InlineData("Phường Bến Nghé", "bennghe")]
    [InlineData("Thị trấn Đông Anh", "donganh")]
    public void Place_names_match_across_spellings(string name, string key) => DivisionNameResolver.Normalise(name).Should().Be(key);

    [Fact]
    public void Gateway_descriptions_are_plain_ascii() =>
        ProviderText.Ascii("Thanh toán đơn hàng ĐƠN-01 — ưu đãi").Should().Be("Thanh toan don hang DON-01  uu dai");
}

public class GhtkWebhookTests
{
    [Fact]
    public void Ghtk_form_webhook_with_the_right_token_is_parsed()
    {
        var carrier = new GhtkCarrier(null!, new GhtkOptions("t", null, "https://x", "hook"), null!, null!);
        var body = "label_id=S1.A1.5&partner_id=SH1&status_id=3&action_time=2026-10-06T10%3A00%3A00.0000000%2B00%3A00&reason_code=&reason=&weight=0.3&fee=26000";
        var hook = new InboundWebhook(new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" },
            new Dictionary<string, string> { ["token"] = "hook" }, body);
        var e = carrier.VerifyWebhook(hook);
        e.Should().NotBeNull();
        e!.TrackingNo.Should().Be("S1.A1.5");
        e.Status.Should().Be(ShipmentStatus.Picked);
        carrier.VerifyWebhook(hook with { Query = new Dictionary<string, string> { ["token"] = "sai" } }).Should().BeNull();
    }
}

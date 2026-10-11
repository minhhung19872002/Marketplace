using FluentAssertions;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.Application;

// G5-B (L192): category names of the sample data are written in sentence case, as the capitalisation policy asks (#209)
public class SeedTextTests
{
    [Theory]
    [InlineData("Điện Thoại & Phụ Kiện", "Điện thoại & phụ kiện")]
    [InlineData("Ô Tô & Xe Máy", "Ô tô & xe máy")]
    [InlineData("Bách Hóa Online", "Bách hóa online")]
    [InlineData("Áo", "Áo")]
    [InlineData("iPad & Tablet", "iPad & tablet")]
    [InlineData("Máy Ảnh DSLR", "Máy ảnh DSLR")]
    [InlineData("Máy Đo SpO2", "Máy đo SpO2")]
    [InlineData("Tai Nghe Bluetooth", "Tai nghe Bluetooth")]
    [InlineData("Lego & Xếp Hình", "Lego & xếp hình")]
    [InlineData("Dầu Cá & Omega-3", "Dầu cá & Omega-3")]
    [InlineData("Sắc đẹp", "Sắc đẹp")]
    public void Writes_a_seed_name_in_sentence_case(string name, string expected) => SeedText.SentenceCase(name).Should().Be(expected);
}

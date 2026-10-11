using FluentAssertions;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.Application;

// G-VIS round 2: clothes and shoes named after their colour get a "Màu Sắc" chip; the promotional cover stays first (L182)
public class SeedColourTests
{
    [Theory]
    [InlineData("Áo Khoác Bomber Nam Đỏ Có Túi Tay", "Đỏ")]
    [InlineData("Áo Sơ Mi Nam Kẻ Caro Xanh Đen Dài Tay", "Xanh Đen")]
    [InlineData("Quần Kaki Nam Xanh Navy Ống Đứng Công Sở", "Xanh Navy")]
    [InlineData("Áo Khoác Kaki Nữ Màu Be Tay Ngắn Cúc Đồng", "Be")]
    [InlineData("Bóng Đá Da PU Khâu Máy Thi Đấu", null)]
    [InlineData("Áo Thun Nam Gaming In Hình Gigabyte Aorus", null)]
    public void Reads_the_colour_of_a_name(string name, string? colour) => ProductGenerator.ColourOf(name).Should().Be(colour);

    [Fact]
    public void Knows_a_promotional_cover()
    {
        ProductGenerator.IsAdCover("p007-ad.webp").Should().BeTrue();
        ProductGenerator.IsAdCover("p007-0.webp").Should().BeFalse();
    }
}

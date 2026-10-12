using System.Text.Json;
using FluentAssertions;
using ShopHub.Infrastructure.Seed;
using ShopHub.UnitTests.SourceScan;

namespace ShopHub.UnitTests.Application;

// G-VIS round 4 (L202): sample product names, variant tiers and options are written in sentence case (#209) like the
// categories (L192), keeping brands, product lines and place names ("Dior", "Galaxy", "Đà Lạt")
public class SeedProductNameTests
{
    [Theory]
    [InlineData("Nước Hoa Dior J'adore Eau De Parfum", "Nước hoa Dior J'adore Eau de Parfum")]
    [InlineData("Áo Thun Nam Gaming In Hình Gigabyte Aorus", "Áo thun nam gaming in hình Gigabyte Aorus")]
    [InlineData("Dâu Tây Đà Lạt Hộp 500g", "Dâu tây Đà Lạt hộp 500g")]
    [InlineData("Điện Thoại Samsung Galaxy S10 Prism Black", "Điện thoại Samsung Galaxy S10 Prism Black")]
    [InlineData("Apple MacBook Pro 14 inch Chip M Space Grey", "Apple MacBook Pro 14 inch chip M Space Grey")]
    [InlineData("Loa Thông Minh Apple HomePod Mini", "Loa thông minh Apple HomePod mini")]
    [InlineData("Chậu Cây Xanh Mini Trang Trí Phòng Khách", "Chậu cây xanh mini trang trí phòng khách")]
    [InlineData("Bộ Sạc iPhone 20W Kèm Cáp USB-C To Lightning", "Bộ sạc iPhone 20W kèm cáp USB-C to Lightning")]
    [InlineData("Bộ Khối Gỗ Chữ Cái Tiếng Anh Cho Bé", "Bộ khối gỗ chữ cái tiếng Anh cho bé")]
    [InlineData("Gậy Cricket Gỗ Liễu Anh Quốc", "Gậy cricket gỗ liễu Anh Quốc")]
    [InlineData("Viên Uống Selen Và Vitamin E Tăng Đề Kháng Lọ 90 Viên", "Viên uống selen và vitamin E tăng đề kháng lọ 90 viên")]
    [InlineData("Cũi Gỗ Thông Sơn Nâu Kèm Nệm Cho Bé", "Cũi gỗ thông sơn nâu kèm nệm cho bé")]
    [InlineData("Hành Tây Tím Lý Sơn", "Hành tây tím Lý Sơn")]
    [InlineData("Xe Đẩy Ba Trong Một Kèm Nôi Và Ghế Ngồi Ô Tô", "Xe đẩy ba trong một kèm nôi và ghế ngồi ô tô")]
    [InlineData("Khẩu Trang Y Tế 4 Lớp Kháng Khuẩn Hộp 50 Cái", "Khẩu trang y tế 4 lớp kháng khuẩn hộp 50 cái")]
    [InlineData("Giày Chạy Bộ Nam Có Đèn LED Ở Gót Chạy Đêm", "Giày chạy bộ nam có đèn LED ở gót chạy đêm")]
    [InlineData("Điện Thoại Apple iPhone X Đen", "Điện thoại Apple iPhone X đen")]
    [InlineData("Đồng Hồ Rolex Cellini Date Mặt Đen Dây Da", "Đồng hồ Rolex Cellini Date mặt đen dây da")]
    public void Writes_a_model_name_in_sentence_case_keeping_proper_names(string name, string expected) =>
        SeedText.ProductName(name).Should().Be(expected);

    [Theory]
    [InlineData("Áo Thun Nữ Cổ Tròn Hồng Pastel Basic", "Mẫu Mới", "Áo thun nữ cổ tròn hồng pastel basic - Mẫu mới")]
    [InlineData("Áo Sơ Mi Flannel Nam Caro Đỏ Đen", "Phong Cách Hàn Quốc", "Áo sơ mi flannel nam caro đỏ đen - Phong cách Hàn Quốc")]
    [InlineData("Điện Thoại Oppo A57 Vàng Hồng", "Đã Qua Sử Dụng 99%", "Điện thoại Oppo A57 vàng hồng - Đã qua sử dụng 99%")]
    public void A_listing_name_is_the_model_and_the_shops_wording_each_in_sentence_case(string model, string wording, string expected) =>
        SeedText.ListingName(model, wording).Should().Be(expected);

    [Fact]
    public void A_sized_model_named_after_its_colour_gets_a_colour_tier_all_in_sentence_case()
    {
        var (colour, main) = ProductGenerator.TierNames("Quần Kaki Nam Xanh Navy Ống Đứng Công Sở", "Kích Cỡ", ["S", "M"]);
        colour!.Name.Should().Be("Màu sắc");
        colour.Options.Should().Equal("Xanh navy");
        main.Name.Should().Be("Kích cỡ");
        main.Options.Should().Equal("S", "M");

        var (none, pack) = ProductGenerator.TierNames("Tã Vải Có Túi Chống Thấm Hoạ Tiết Vòng Tròn", "Gói", ["Gói 1 Tã", "Combo 3 Tã"]);
        none.Should().BeNull();
        pack.Options.Should().Equal("Gói 1 tã", "Combo 3 tã");
    }

    [Fact]
    public void Every_sample_model_name_keeps_only_listed_proper_names_capitalised()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Seed", "Data",
            "product-models.json")));
        var names = doc.RootElement.EnumerateArray().Select(m => SeedText.ProductName(m.GetProperty("name").GetString()!)).ToList();
        // A word after the first one that starts with a capital is a proper name, an acronym, a model number or "iPhone"-like
        var stray = names.SelectMany(n => n.Split(' ').Skip(1))
            .Where(w => w.Length > 1 && char.IsUpper(w[0]) && !w.Skip(1).Any(c => char.IsUpper(c) || char.IsDigit(c)) && !SeedText.IsProperName(w))
            .Distinct().ToList();
        stray.Should().BeEmpty();
        names.Should().Contain("Nước hoa Chanel Coco Noir Eau de Parfum");
    }
}

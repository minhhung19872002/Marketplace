using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SentenceCaseSeedNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G5-B (L192): the sample categories and home shortcuts were seeded in Title Case ("Sắc Đẹp", "Mã Giảm Giá"); the
            // seeder now writes sentence case (SeedText, #209). Rows still holding a seed name are renamed; names an admin
            // changed match no pair and are left alone. Every pair is SeedText.SentenceCase of its old name
            // (SentenceCaseMigrationTests).
            migrationBuilder.Sql("""
                UPDATE catalog.categories c SET name = v.new_name, updated_at = now()
                FROM (VALUES
                    ('Thời Trang Nam', 'Thời trang nam'), ('Áo Thun', 'Áo thun'), ('Áo Sơ Mi', 'Áo sơ mi'),
                    ('Áo Khoác', 'Áo khoác'), ('Áo Polo', 'Áo polo'), ('Quần Jean', 'Quần jean'), ('Quần Kaki', 'Quần kaki'),
                    ('Quần Short', 'Quần short'), ('Phụ Kiện Nam', 'Phụ kiện nam'), ('Kính Mắt', 'Kính mắt'),
                    ('Ví Nam', 'Ví nam'), ('Thắt Lưng', 'Thắt lưng'), ('Thời Trang Nữ', 'Thời trang nữ'), ('Váy Đầm', 'Váy đầm'),
                    ('Đầm Suông', 'Đầm suông'), ('Đầm Dự Tiệc', 'Đầm dự tiệc'), ('Chân Váy', 'Chân váy'), ('Áo Nữ', 'Áo nữ'),
                    ('Áo Kiểu', 'Áo kiểu'), ('Áo Thun Nữ', 'Áo thun nữ'), ('Áo Khoác Nữ', 'Áo khoác nữ'), ('Quần Nữ', 'Quần nữ'),
                    ('Quần Jean Nữ', 'Quần jean nữ'), ('Quần Ống Rộng', 'Quần ống rộng'), ('Phụ Kiện Nữ', 'Phụ kiện nữ'),
                    ('Bông Tai', 'Bông tai'), ('Điện Thoại & Phụ Kiện', 'Điện thoại & phụ kiện'), ('Điện Thoại', 'Điện thoại'),
                    ('Điện Thoại Thông Minh', 'Điện thoại thông minh'), ('Điện Thoại Phổ Thông', 'Điện thoại phổ thông'),
                    ('Phụ Kiện Điện Thoại', 'Phụ kiện điện thoại'), ('Sạc Dự Phòng', 'Sạc dự phòng'), ('Ốp Lưng', 'Ốp lưng'),
                    ('Cáp & Sạc', 'Cáp & sạc'), ('Kính Cường Lực', 'Kính cường lực'),
                    ('Gậy Chụp Ảnh & Livestream', 'Gậy chụp ảnh & livestream'), ('Máy Tính & Laptop', 'Máy tính & laptop'),
                    ('Laptop Văn Phòng', 'Laptop văn phòng'), ('Laptop Gaming', 'Laptop gaming'),
                    ('Phụ Kiện Máy Tính', 'Phụ kiện máy tính'), ('Bàn Phím', 'Bàn phím'),
                    ('Combo Phím Chuột', 'Combo phím chuột'), ('Balo & Túi Laptop', 'Balo & túi laptop'),
                    ('Máy Tính Bảng', 'Máy tính bảng'), ('iPad & Tablet', 'iPad & tablet'),
                    ('Thiết Bị Điện Tử', 'Thiết bị điện tử'), ('Thiết Bị Âm Thanh', 'Thiết bị âm thanh'),
                    ('Tai Nghe Bluetooth', 'Tai nghe Bluetooth'), ('Tai Nghe Có Dây', 'Tai nghe có dây'),
                    ('Loa Thông Minh', 'Loa thông minh'), ('Thiết Bị Đeo Thông Minh', 'Thiết bị đeo thông minh'),
                    ('Vòng Đeo Tay Thông Minh', 'Vòng đeo tay thông minh'), ('Máy Ảnh & Quay Phim', 'Máy ảnh & quay phim'),
                    ('Máy Ảnh', 'Máy ảnh'), ('Máy Ảnh Mirrorless', 'Máy ảnh mirrorless'), ('Máy Ảnh Compact', 'Máy ảnh compact'),
                    ('Máy Ảnh DSLR', 'Máy ảnh DSLR'), ('Phụ Kiện Máy Ảnh', 'Phụ kiện máy ảnh'), ('Thẻ Nhớ', 'Thẻ nhớ'),
                    ('Chân Máy', 'Chân máy'), ('Ống Kính', 'Ống kính'), ('Máy Quay', 'Máy quay'),
                    ('Máy Quay Phim', 'Máy quay phim'), ('Đồng Hồ', 'Đồng hồ'), ('Đồng Hồ Nam', 'Đồng hồ nam'),
                    ('Đồng Hồ Kim Loại', 'Đồng hồ kim loại'), ('Đồng Hồ Dây Da', 'Đồng hồ dây da'),
                    ('Đồng Hồ Thông Minh', 'Đồng hồ thông minh'), ('Đồng Hồ Nữ', 'Đồng hồ nữ'),
                    ('Đồng Hồ Nữ Dây Da', 'Đồng hồ nữ dây da'), ('Đồng Hồ Nữ Kim Loại', 'Đồng hồ nữ kim loại'),
                    ('Giày Dép Nam', 'Giày dép nam'), ('Giày Thể Thao Nam', 'Giày thể thao nam'),
                    ('Giày Chạy Bộ', 'Giày chạy bộ'), ('Giày Tây', 'Giày tây'), ('Giày Da Nam', 'Giày da nam'),
                    ('Giày Lười Nam', 'Giày lười nam'), ('Dép Nam', 'Dép nam'), ('Dép Quai Ngang', 'Dép quai ngang'),
                    ('Dép Xỏ Ngón Nam', 'Dép xỏ ngón nam'), ('Giày Dép Nữ', 'Giày dép nữ'), ('Giày Nữ', 'Giày nữ'),
                    ('Giày Cao Gót', 'Giày cao gót'), ('Giày Búp Bê', 'Giày búp bê'), ('Sneaker Nữ', 'Sneaker nữ'),
                    ('Bốt Nữ', 'Bốt nữ'), ('Giày Loafer Nữ', 'Giày loafer nữ'), ('Dép Nữ', 'Dép nữ'), ('Sandal Nữ', 'Sandal nữ'),
                    ('Dép Xỏ Ngón Nữ', 'Dép xỏ ngón nữ'), ('Túi Ví Nữ', 'Túi ví nữ'), ('Túi Xách', 'Túi xách'),
                    ('Túi Xách Tay', 'Túi xách tay'), ('Túi Đeo Chéo', 'Túi đeo chéo'), ('Ví Nữ', 'Ví nữ'),
                    ('Ví Cầm Tay', 'Ví cầm tay'), ('Balo Nữ', 'Balo nữ'), ('Balo Thời Trang', 'Balo thời trang'),
                    ('Mẹ & Bé', 'Mẹ & bé'), ('Tã & Bỉm', 'Tã & bỉm'), ('Tã Dán', 'Tã dán'), ('Tã Quần', 'Tã quần'),
                    ('Tã Vải', 'Tã vải'), ('Đồ Dùng Cho Bé', 'Đồ dùng cho bé'), ('Bình Sữa', 'Bình sữa'), ('Xe Đẩy', 'Xe đẩy'),
                    ('Núm Ti Giả', 'Núm ti giả'), ('Ghế Ăn Dặm', 'Ghế ăn dặm'), ('Nôi & Cũi', 'Nôi & cũi'),
                    ('Ghế Ngồi Ô Tô', 'Ghế ngồi ô tô'), ('Thời Trang Bé', 'Thời trang bé'), ('Giày Tập Đi', 'Giày tập đi'),
                    ('Nhà Cửa & Đời Sống', 'Nhà cửa & đời sống'), ('Đồ Dùng Nhà Bếp', 'Đồ dùng nhà bếp'),
                    ('Nồi Chiên Không Dầu', 'Nồi chiên không dầu'), ('Nồi & Chảo', 'Nồi & chảo'), ('Máy Xay', 'Máy xay'),
                    ('Bình Giữ Nhiệt', 'Bình giữ nhiệt'), ('Dụng Cụ Nấu Ăn', 'Dụng cụ nấu ăn'), ('Dao & Thớt', 'Dao & thớt'),
                    ('Thiết Bị Bếp Điện', 'Thiết bị bếp điện'), ('Bát Đĩa & Dao Dĩa', 'Bát đĩa & dao dĩa'),
                    ('Nồi Cơm Điện', 'Nồi cơm điện'), ('Đèn Bàn', 'Đèn bàn'), ('Đèn Ngủ', 'Đèn ngủ'),
                    ('Chăn Ga Gối', 'Chăn ga gối'), ('Chăn Mền', 'Chăn mền'), ('Nội Thất', 'Nội thất'),
                    ('Giường & Sofa', 'Giường & sofa'), ('Bàn Ghế', 'Bàn ghế'), ('Phòng Tắm', 'Phòng tắm'),
                    ('Trang Trí', 'Trang trí'), ('Đồ Trang Trí', 'Đồ trang trí'), ('Cây & Chậu Cảnh', 'Cây & chậu cảnh'),
                    ('Sắc Đẹp', 'Sắc đẹp'), ('Trang Điểm', 'Trang điểm'), ('Son Môi', 'Son môi'), ('Kem Nền', 'Kem nền'),
                    ('Trang Điểm Mắt', 'Trang điểm mắt'), ('Sơn Móng Tay', 'Sơn móng tay'), ('Chăm Sóc Da', 'Chăm sóc da'),
                    ('Kem Chống Nắng', 'Kem chống nắng'), ('Sữa Rửa Mặt', 'Sữa rửa mặt'), ('Nước Hoa', 'Nước hoa'),
                    ('Nước Hoa Nữ', 'Nước hoa nữ'), ('Nước Hoa Nam', 'Nước hoa nam'), ('Chăm Sóc Cơ Thể', 'Chăm sóc cơ thể'),
                    ('Sữa Tắm & Xà Phòng', 'Sữa tắm & xà phòng'), ('Dưỡng Thể', 'Dưỡng thể'), ('Sức Khỏe', 'Sức khỏe'),
                    ('Thực Phẩm Chức Năng', 'Thực phẩm chức năng'), ('Whey Protein', 'Whey protein'),
                    ('Dầu Cá & Omega-3', 'Dầu cá & Omega-3'), ('Thiết Bị Y Tế', 'Thiết bị y tế'),
                    ('Máy Đo Huyết Áp', 'Máy đo huyết áp'), ('Nhiệt Kế', 'Nhiệt kế'), ('Máy Đo SpO2', 'Máy đo SpO2'),
                    ('Vật Tư Y Tế', 'Vật tư y tế'), ('Khẩu Trang', 'Khẩu trang'), ('Thể Thao & Du Lịch', 'Thể thao & du lịch'),
                    ('Dụng Cụ Thể Thao', 'Dụng cụ thể thao'), ('Cầu Lông', 'Cầu lông'), ('Bóng Đá', 'Bóng đá'),
                    ('Bóng Bầu Dục', 'Bóng bầu dục'), ('Bóng Chày', 'Bóng chày'), ('Bóng Rổ', 'Bóng rổ'),
                    ('Bóng Chuyền', 'Bóng chuyền'), ('Du Lịch', 'Du lịch'), ('Balo Du Lịch', 'Balo du lịch'),
                    ('Ô Tô & Xe Máy', 'Ô tô & xe máy'), ('Phụ Kiện Xe Máy', 'Phụ kiện xe máy'), ('Mũ Bảo Hiểm', 'Mũ bảo hiểm'),
                    ('Áo Mưa', 'Áo mưa'), ('Phụ Kiện Ô Tô', 'Phụ kiện ô tô'), ('Camera Hành Trình', 'Camera hành trình'),
                    ('Xe Máy', 'Xe máy'), ('Xe Mô Tô', 'Xe mô tô'), ('Xe Tay Ga', 'Xe tay ga'), ('Đồ Chơi', 'Đồ chơi'),
                    ('Đồ Chơi Lắp Ráp', 'Đồ chơi lắp ráp'), ('Lego & Xếp Hình', 'Lego & xếp hình'),
                    ('Đồ Chơi Vận Động', 'Đồ chơi vận động'), ('Xe Đạp Trẻ Em', 'Xe đạp trẻ em'), ('Xe Tập Đi', 'Xe tập đi'),
                    ('Đồ Chơi Mềm', 'Đồ chơi mềm'), ('Gấu Bông', 'Gấu bông'), ('Đồ Chơi Giáo Dục', 'Đồ chơi giáo dục'),
                    ('Đồ Chơi Trí Tuệ', 'Đồ chơi trí tuệ'), ('Mô Hình', 'Mô hình'), ('Xe Mô Hình', 'Xe mô hình'),
                    ('Bách Hóa Online', 'Bách hóa online'), ('Thực Phẩm Khô', 'Thực phẩm khô'), ('Mì & Bún', 'Mì & bún'),
                    ('Dầu Ăn & Gia Vị', 'Dầu ăn & gia vị'), ('Đồ Uống', 'Đồ uống'), ('Cà Phê', 'Cà phê'),
                    ('Kem & Đồ Ngọt', 'Kem & đồ ngọt'), ('Nước Giải Khát', 'Nước giải khát'),
                    ('Thực Phẩm Tươi', 'Thực phẩm tươi'), ('Trái Cây', 'Trái cây'), ('Rau Củ', 'Rau củ'),
                    ('Thịt & Hải Sản', 'Thịt & hải sản'), ('Trứng & Sữa', 'Trứng & sữa'),
                    ('Đồ Dùng Gia Đình', 'Đồ dùng gia đình'), ('Khăn Giấy', 'Khăn giấy'), ('Thú Cưng', 'Thú cưng'),
                    ('Thức Ăn Thú Cưng', 'Thức ăn thú cưng'), ('Thức Ăn Cho Mèo', 'Thức ăn cho mèo'),
                    ('Thức Ăn Cho Chó', 'Thức ăn cho chó'), ('Phụ Kiện Thú Cưng', 'Phụ kiện thú cưng'),
                    ('Bát Ăn & Bình Nước', 'Bát ăn & bình nước'), ('Vòng Cổ & Dây Dắt', 'Vòng cổ & dây dắt'),
                    ('Giường & Nệm', 'Giường & nệm')) AS v(old_name, new_name)
                WHERE c.name = v.old_name;

                UPDATE promo.banners b SET title = v.new_name
                FROM (VALUES
                    ('Mã Giảm Giá', 'Mã giảm giá'), ('Deal Sốc', 'Deal sốc'), ('Shop Yêu Thích', 'Shop yêu thích'),
                    ('Hàng 4 Sao', 'Hàng 4 sao'), ('Bán Chạy', 'Bán chạy'), ('Hàng Mới Về', 'Hàng mới về'),
                    ('Giá Từ Thấp', 'Giá từ thấp'), ('Có Sẵn Hàng', 'Có sẵn hàng')) AS v(old_name, new_name)
                WHERE b.position = 'Shortcut' AND b.title = v.old_name;

                -- Banner ngành seeded as "<category> — …" (G5-B)
                UPDATE promo.banners b SET title = v.new_name || substr(b.title, length(v.old_name) + 1)
                FROM (VALUES
                    ('Thời Trang Nam', 'Thời trang nam'), ('Thời Trang Nữ', 'Thời trang nữ'),
                    ('Điện Thoại & Phụ Kiện', 'Điện thoại & phụ kiện'), ('Máy Tính & Laptop', 'Máy tính & laptop'),
                    ('Thiết Bị Điện Tử', 'Thiết bị điện tử'), ('Máy Ảnh & Quay Phim', 'Máy ảnh & quay phim'),
                    ('Đồng Hồ', 'Đồng hồ'), ('Giày Dép Nam', 'Giày dép nam'), ('Giày Dép Nữ', 'Giày dép nữ'),
                    ('Túi Ví Nữ', 'Túi ví nữ'), ('Mẹ & Bé', 'Mẹ & bé'), ('Nhà Cửa & Đời Sống', 'Nhà cửa & đời sống'),
                    ('Sắc Đẹp', 'Sắc đẹp'), ('Sức Khỏe', 'Sức khỏe'), ('Thể Thao & Du Lịch', 'Thể thao & du lịch'),
                    ('Ô Tô & Xe Máy', 'Ô tô & xe máy'), ('Đồ Chơi', 'Đồ chơi'), ('Bách Hóa Online', 'Bách hóa online'),
                    ('Thú Cưng', 'Thú cưng')) AS v(old_name, new_name)
                WHERE b.position = 'Category' AND b.title LIKE v.old_name || ' — %';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE catalog.categories c SET name = v.new_name, updated_at = now()
                FROM (VALUES
                    ('Thời trang nam', 'Thời Trang Nam'), ('Áo thun', 'Áo Thun'), ('Áo sơ mi', 'Áo Sơ Mi'),
                    ('Áo khoác', 'Áo Khoác'), ('Áo polo', 'Áo Polo'), ('Quần jean', 'Quần Jean'), ('Quần kaki', 'Quần Kaki'),
                    ('Quần short', 'Quần Short'), ('Phụ kiện nam', 'Phụ Kiện Nam'), ('Kính mắt', 'Kính Mắt'),
                    ('Ví nam', 'Ví Nam'), ('Thắt lưng', 'Thắt Lưng'), ('Thời trang nữ', 'Thời Trang Nữ'), ('Váy đầm', 'Váy Đầm'),
                    ('Đầm suông', 'Đầm Suông'), ('Đầm dự tiệc', 'Đầm Dự Tiệc'), ('Chân váy', 'Chân Váy'), ('Áo nữ', 'Áo Nữ'),
                    ('Áo kiểu', 'Áo Kiểu'), ('Áo thun nữ', 'Áo Thun Nữ'), ('Áo khoác nữ', 'Áo Khoác Nữ'), ('Quần nữ', 'Quần Nữ'),
                    ('Quần jean nữ', 'Quần Jean Nữ'), ('Quần ống rộng', 'Quần Ống Rộng'), ('Phụ kiện nữ', 'Phụ Kiện Nữ'),
                    ('Bông tai', 'Bông Tai'), ('Điện thoại & phụ kiện', 'Điện Thoại & Phụ Kiện'), ('Điện thoại', 'Điện Thoại'),
                    ('Điện thoại thông minh', 'Điện Thoại Thông Minh'), ('Điện thoại phổ thông', 'Điện Thoại Phổ Thông'),
                    ('Phụ kiện điện thoại', 'Phụ Kiện Điện Thoại'), ('Sạc dự phòng', 'Sạc Dự Phòng'), ('Ốp lưng', 'Ốp Lưng'),
                    ('Cáp & sạc', 'Cáp & Sạc'), ('Kính cường lực', 'Kính Cường Lực'),
                    ('Gậy chụp ảnh & livestream', 'Gậy Chụp Ảnh & Livestream'), ('Máy tính & laptop', 'Máy Tính & Laptop'),
                    ('Laptop văn phòng', 'Laptop Văn Phòng'), ('Laptop gaming', 'Laptop Gaming'),
                    ('Phụ kiện máy tính', 'Phụ Kiện Máy Tính'), ('Bàn phím', 'Bàn Phím'),
                    ('Combo phím chuột', 'Combo Phím Chuột'), ('Balo & túi laptop', 'Balo & Túi Laptop'),
                    ('Máy tính bảng', 'Máy Tính Bảng'), ('iPad & tablet', 'iPad & Tablet'),
                    ('Thiết bị điện tử', 'Thiết Bị Điện Tử'), ('Thiết bị âm thanh', 'Thiết Bị Âm Thanh'),
                    ('Tai nghe Bluetooth', 'Tai Nghe Bluetooth'), ('Tai nghe có dây', 'Tai Nghe Có Dây'),
                    ('Loa thông minh', 'Loa Thông Minh'), ('Thiết bị đeo thông minh', 'Thiết Bị Đeo Thông Minh'),
                    ('Vòng đeo tay thông minh', 'Vòng Đeo Tay Thông Minh'), ('Máy ảnh & quay phim', 'Máy Ảnh & Quay Phim'),
                    ('Máy ảnh', 'Máy Ảnh'), ('Máy ảnh mirrorless', 'Máy Ảnh Mirrorless'), ('Máy ảnh compact', 'Máy Ảnh Compact'),
                    ('Máy ảnh DSLR', 'Máy Ảnh DSLR'), ('Phụ kiện máy ảnh', 'Phụ Kiện Máy Ảnh'), ('Thẻ nhớ', 'Thẻ Nhớ'),
                    ('Chân máy', 'Chân Máy'), ('Ống kính', 'Ống Kính'), ('Máy quay', 'Máy Quay'),
                    ('Máy quay phim', 'Máy Quay Phim'), ('Đồng hồ', 'Đồng Hồ'), ('Đồng hồ nam', 'Đồng Hồ Nam'),
                    ('Đồng hồ kim loại', 'Đồng Hồ Kim Loại'), ('Đồng hồ dây da', 'Đồng Hồ Dây Da'),
                    ('Đồng hồ thông minh', 'Đồng Hồ Thông Minh'), ('Đồng hồ nữ', 'Đồng Hồ Nữ'),
                    ('Đồng hồ nữ dây da', 'Đồng Hồ Nữ Dây Da'), ('Đồng hồ nữ kim loại', 'Đồng Hồ Nữ Kim Loại'),
                    ('Giày dép nam', 'Giày Dép Nam'), ('Giày thể thao nam', 'Giày Thể Thao Nam'),
                    ('Giày chạy bộ', 'Giày Chạy Bộ'), ('Giày tây', 'Giày Tây'), ('Giày da nam', 'Giày Da Nam'),
                    ('Giày lười nam', 'Giày Lười Nam'), ('Dép nam', 'Dép Nam'), ('Dép quai ngang', 'Dép Quai Ngang'),
                    ('Dép xỏ ngón nam', 'Dép Xỏ Ngón Nam'), ('Giày dép nữ', 'Giày Dép Nữ'), ('Giày nữ', 'Giày Nữ'),
                    ('Giày cao gót', 'Giày Cao Gót'), ('Giày búp bê', 'Giày Búp Bê'), ('Sneaker nữ', 'Sneaker Nữ'),
                    ('Bốt nữ', 'Bốt Nữ'), ('Giày loafer nữ', 'Giày Loafer Nữ'), ('Dép nữ', 'Dép Nữ'), ('Sandal nữ', 'Sandal Nữ'),
                    ('Dép xỏ ngón nữ', 'Dép Xỏ Ngón Nữ'), ('Túi ví nữ', 'Túi Ví Nữ'), ('Túi xách', 'Túi Xách'),
                    ('Túi xách tay', 'Túi Xách Tay'), ('Túi đeo chéo', 'Túi Đeo Chéo'), ('Ví nữ', 'Ví Nữ'),
                    ('Ví cầm tay', 'Ví Cầm Tay'), ('Balo nữ', 'Balo Nữ'), ('Balo thời trang', 'Balo Thời Trang'),
                    ('Mẹ & bé', 'Mẹ & Bé'), ('Tã & bỉm', 'Tã & Bỉm'), ('Tã dán', 'Tã Dán'), ('Tã quần', 'Tã Quần'),
                    ('Tã vải', 'Tã Vải'), ('Đồ dùng cho bé', 'Đồ Dùng Cho Bé'), ('Bình sữa', 'Bình Sữa'), ('Xe đẩy', 'Xe Đẩy'),
                    ('Núm ti giả', 'Núm Ti Giả'), ('Ghế ăn dặm', 'Ghế Ăn Dặm'), ('Nôi & cũi', 'Nôi & Cũi'),
                    ('Ghế ngồi ô tô', 'Ghế Ngồi Ô Tô'), ('Thời trang bé', 'Thời Trang Bé'), ('Giày tập đi', 'Giày Tập Đi'),
                    ('Nhà cửa & đời sống', 'Nhà Cửa & Đời Sống'), ('Đồ dùng nhà bếp', 'Đồ Dùng Nhà Bếp'),
                    ('Nồi chiên không dầu', 'Nồi Chiên Không Dầu'), ('Nồi & chảo', 'Nồi & Chảo'), ('Máy xay', 'Máy Xay'),
                    ('Bình giữ nhiệt', 'Bình Giữ Nhiệt'), ('Dụng cụ nấu ăn', 'Dụng Cụ Nấu Ăn'), ('Dao & thớt', 'Dao & Thớt'),
                    ('Thiết bị bếp điện', 'Thiết Bị Bếp Điện'), ('Bát đĩa & dao dĩa', 'Bát Đĩa & Dao Dĩa'),
                    ('Nồi cơm điện', 'Nồi Cơm Điện'), ('Đèn bàn', 'Đèn Bàn'), ('Đèn ngủ', 'Đèn Ngủ'),
                    ('Chăn ga gối', 'Chăn Ga Gối'), ('Chăn mền', 'Chăn Mền'), ('Nội thất', 'Nội Thất'),
                    ('Giường & sofa', 'Giường & Sofa'), ('Bàn ghế', 'Bàn Ghế'), ('Phòng tắm', 'Phòng Tắm'),
                    ('Trang trí', 'Trang Trí'), ('Đồ trang trí', 'Đồ Trang Trí'), ('Cây & chậu cảnh', 'Cây & Chậu Cảnh'),
                    ('Sắc đẹp', 'Sắc Đẹp'), ('Trang điểm', 'Trang Điểm'), ('Son môi', 'Son Môi'), ('Kem nền', 'Kem Nền'),
                    ('Trang điểm mắt', 'Trang Điểm Mắt'), ('Sơn móng tay', 'Sơn Móng Tay'), ('Chăm sóc da', 'Chăm Sóc Da'),
                    ('Kem chống nắng', 'Kem Chống Nắng'), ('Sữa rửa mặt', 'Sữa Rửa Mặt'), ('Nước hoa', 'Nước Hoa'),
                    ('Nước hoa nữ', 'Nước Hoa Nữ'), ('Nước hoa nam', 'Nước Hoa Nam'), ('Chăm sóc cơ thể', 'Chăm Sóc Cơ Thể'),
                    ('Sữa tắm & xà phòng', 'Sữa Tắm & Xà Phòng'), ('Dưỡng thể', 'Dưỡng Thể'), ('Sức khỏe', 'Sức Khỏe'),
                    ('Thực phẩm chức năng', 'Thực Phẩm Chức Năng'), ('Whey protein', 'Whey Protein'),
                    ('Dầu cá & Omega-3', 'Dầu Cá & Omega-3'), ('Thiết bị y tế', 'Thiết Bị Y Tế'),
                    ('Máy đo huyết áp', 'Máy Đo Huyết Áp'), ('Nhiệt kế', 'Nhiệt Kế'), ('Máy đo SpO2', 'Máy Đo SpO2'),
                    ('Vật tư y tế', 'Vật Tư Y Tế'), ('Khẩu trang', 'Khẩu Trang'), ('Thể thao & du lịch', 'Thể Thao & Du Lịch'),
                    ('Dụng cụ thể thao', 'Dụng Cụ Thể Thao'), ('Cầu lông', 'Cầu Lông'), ('Bóng đá', 'Bóng Đá'),
                    ('Bóng bầu dục', 'Bóng Bầu Dục'), ('Bóng chày', 'Bóng Chày'), ('Bóng rổ', 'Bóng Rổ'),
                    ('Bóng chuyền', 'Bóng Chuyền'), ('Du lịch', 'Du Lịch'), ('Balo du lịch', 'Balo Du Lịch'),
                    ('Ô tô & xe máy', 'Ô Tô & Xe Máy'), ('Phụ kiện xe máy', 'Phụ Kiện Xe Máy'), ('Mũ bảo hiểm', 'Mũ Bảo Hiểm'),
                    ('Áo mưa', 'Áo Mưa'), ('Phụ kiện ô tô', 'Phụ Kiện Ô Tô'), ('Camera hành trình', 'Camera Hành Trình'),
                    ('Xe máy', 'Xe Máy'), ('Xe mô tô', 'Xe Mô Tô'), ('Xe tay ga', 'Xe Tay Ga'), ('Đồ chơi', 'Đồ Chơi'),
                    ('Đồ chơi lắp ráp', 'Đồ Chơi Lắp Ráp'), ('Lego & xếp hình', 'Lego & Xếp Hình'),
                    ('Đồ chơi vận động', 'Đồ Chơi Vận Động'), ('Xe đạp trẻ em', 'Xe Đạp Trẻ Em'), ('Xe tập đi', 'Xe Tập Đi'),
                    ('Đồ chơi mềm', 'Đồ Chơi Mềm'), ('Gấu bông', 'Gấu Bông'), ('Đồ chơi giáo dục', 'Đồ Chơi Giáo Dục'),
                    ('Đồ chơi trí tuệ', 'Đồ Chơi Trí Tuệ'), ('Mô hình', 'Mô Hình'), ('Xe mô hình', 'Xe Mô Hình'),
                    ('Bách hóa online', 'Bách Hóa Online'), ('Thực phẩm khô', 'Thực Phẩm Khô'), ('Mì & bún', 'Mì & Bún'),
                    ('Dầu ăn & gia vị', 'Dầu Ăn & Gia Vị'), ('Đồ uống', 'Đồ Uống'), ('Cà phê', 'Cà Phê'),
                    ('Kem & đồ ngọt', 'Kem & Đồ Ngọt'), ('Nước giải khát', 'Nước Giải Khát'),
                    ('Thực phẩm tươi', 'Thực Phẩm Tươi'), ('Trái cây', 'Trái Cây'), ('Rau củ', 'Rau Củ'),
                    ('Thịt & hải sản', 'Thịt & Hải Sản'), ('Trứng & sữa', 'Trứng & Sữa'),
                    ('Đồ dùng gia đình', 'Đồ Dùng Gia Đình'), ('Khăn giấy', 'Khăn Giấy'), ('Thú cưng', 'Thú Cưng'),
                    ('Thức ăn thú cưng', 'Thức Ăn Thú Cưng'), ('Thức ăn cho mèo', 'Thức Ăn Cho Mèo'),
                    ('Thức ăn cho chó', 'Thức Ăn Cho Chó'), ('Phụ kiện thú cưng', 'Phụ Kiện Thú Cưng'),
                    ('Bát ăn & bình nước', 'Bát Ăn & Bình Nước'), ('Vòng cổ & dây dắt', 'Vòng Cổ & Dây Dắt'),
                    ('Giường & nệm', 'Giường & Nệm')) AS v(old_name, new_name)
                WHERE c.name = v.old_name;

                UPDATE promo.banners b SET title = v.new_name
                FROM (VALUES
                    ('Mã giảm giá', 'Mã Giảm Giá'), ('Deal sốc', 'Deal Sốc'), ('Shop yêu thích', 'Shop Yêu Thích'),
                    ('Hàng 4 sao', 'Hàng 4 Sao'), ('Bán chạy', 'Bán Chạy'), ('Hàng mới về', 'Hàng Mới Về'),
                    ('Giá từ thấp', 'Giá Từ Thấp'), ('Có sẵn hàng', 'Có Sẵn Hàng')) AS v(old_name, new_name)
                WHERE b.position = 'Shortcut' AND b.title = v.old_name;

                -- Banner ngành seeded as "<category> — …" (G5-B)
                UPDATE promo.banners b SET title = v.new_name || substr(b.title, length(v.old_name) + 1)
                FROM (VALUES
                    ('Thời trang nam', 'Thời Trang Nam'), ('Thời trang nữ', 'Thời Trang Nữ'),
                    ('Điện thoại & phụ kiện', 'Điện Thoại & Phụ Kiện'), ('Máy tính & laptop', 'Máy Tính & Laptop'),
                    ('Thiết bị điện tử', 'Thiết Bị Điện Tử'), ('Máy ảnh & quay phim', 'Máy Ảnh & Quay Phim'),
                    ('Đồng hồ', 'Đồng Hồ'), ('Giày dép nam', 'Giày Dép Nam'), ('Giày dép nữ', 'Giày Dép Nữ'),
                    ('Túi ví nữ', 'Túi Ví Nữ'), ('Mẹ & bé', 'Mẹ & Bé'), ('Nhà cửa & đời sống', 'Nhà Cửa & Đời Sống'),
                    ('Sắc đẹp', 'Sắc Đẹp'), ('Sức khỏe', 'Sức Khỏe'), ('Thể thao & du lịch', 'Thể Thao & Du Lịch'),
                    ('Ô tô & xe máy', 'Ô Tô & Xe Máy'), ('Đồ chơi', 'Đồ Chơi'), ('Bách hóa online', 'Bách Hóa Online'),
                    ('Thú cưng', 'Thú Cưng')) AS v(old_name, new_name)
                WHERE b.position = 'Category' AND b.title LIKE v.old_name || ' — %';
                """);
        }
    }
}

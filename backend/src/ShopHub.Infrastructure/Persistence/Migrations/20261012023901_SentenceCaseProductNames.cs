using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SentenceCaseProductNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G-VIS round 4 (L202): sample product names, their descriptions' first line and variant tiers / options were
            // seeded in Title Case ("Nước Hoa Dior J'adore Eau De Parfum", "Dung Tích", "Xanh Navy"); the seeder now writes
            // sentence case keeping brands, product lines and places (SeedText.ProductName / SentenceCase, #209). A product is
            // renamed only while its name is still a seed name — the model's, or the model's then " - " and a shop wording; a
            // name a seller changed matches no pair and the product is left alone, tiers and description included. Every pair
            // is what the seeder writes now (SentenceCaseProductNamesMigrationTests). Renamed products are queued for search.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE l202_m (old_v text, new_v text);
                INSERT INTO l202_m VALUES
                    ('Mascara Essence Lash Princess Làm Dày Và Cong Mi', 'Mascara Essence Lash Princess làm dày và cong mi'),
                    ('Bảng Phấn Mắt 12 Ô Kèm Gương', 'Bảng phấn mắt 12 ô kèm gương'),
                    ('Phấn Phủ Nén Kiềm Dầu Mịn Nhẹ', 'Phấn phủ nén kiềm dầu mịn nhẹ'), ('Son Thỏi Lì Màu Đỏ Cổ Điển', 'Son thỏi lì màu đỏ cổ điển'),
                    ('Sơn Móng Tay Đỏ Bóng Lâu Trôi', 'Sơn móng tay đỏ bóng lâu trôi'),
                    ('Nước Hoa Calvin Klein CK One Eau De Toilette', 'Nước hoa Calvin Klein CK One Eau de Toilette'),
                    ('Nước Hoa Chanel Coco Noir Eau De Parfum', 'Nước hoa Chanel Coco Noir Eau de Parfum'),
                    ('Nước Hoa Dior J''adore Eau De Parfum', 'Nước hoa Dior J''adore Eau de Parfum'),
                    ('Nước Hoa Dolce & Gabbana Dolce Shine', 'Nước hoa Dolce & Gabbana Dolce Shine'),
                    ('Nước Hoa Gucci Bloom Eau De Parfum', 'Nước hoa Gucci Bloom Eau de Parfum'),
                    ('Nước Rửa Tay Attitude Super Leaves Hương Chanh', 'Nước rửa tay Attitude Super Leaves hương chanh'),
                    ('Sữa Tắm Olay Ultra Moisture Bơ Hạt Mỡ 700ml', 'Sữa tắm Olay Ultra Moisture bơ hạt mỡ 700ml'),
                    ('Sữa Dưỡng Thể Vaseline Men Cho Da Mặt Và Body', 'Sữa dưỡng thể Vaseline Men cho da mặt và body'),
                    ('Giường Ngủ Gỗ Tự Nhiên Đầu Giường Bọc Nệm 1m8', 'Giường ngủ gỗ tự nhiên đầu giường bọc nệm 1m8'),
                    ('Sofa Văng Vải Bố 3 Chỗ Phong Cách Ý', 'Sofa văng vải bố 3 chỗ phong cách Ý'),
                    ('Tủ Đầu Giường Gỗ Gụ 2 Ngăn Kéo', 'Tủ đầu giường gỗ gụ 2 ngăn kéo'),
                    ('Ghế Xoay Văn Phòng Bọc Nỉ Chân Nhôm', 'Ghế xoay văn phòng bọc nỉ chân nhôm'),
                    ('Tủ Lavabo Gỗ Kèm Gương Phòng Tắm', 'Tủ lavabo gỗ kèm gương phòng tắm'),
                    ('Ghế Xích Đu Mây Treo Ban Công Kèm Nệm', 'Ghế xích đu mây treo ban công kèm nệm'),
                    ('Khung Ảnh Treo Tường Hình Cây Gia Đình 12 Ô', 'Khung ảnh treo tường hình cây gia đình 12 ô'),
                    ('Cây Cảnh Để Bàn Chậu Đứng Kim Loại', 'Cây cảnh để bàn chậu đứng kim loại'),
                    ('Chậu Cây Xanh Mini Trang Trí Phòng Khách', 'Chậu cây xanh mini trang trí phòng khách'),
                    ('Đèn Bàn Chụp Vải Chân Gỗ Cổ Điển', 'Đèn bàn chụp vải chân gỗ cổ điển'),
                    ('Xẻng Xào Gỗ Tre Chịu Nhiệt', 'Xẻng xào gỗ tre chịu nhiệt'),
                    ('Ly Giữ Nhiệt Nhôm Đen Có Quai 400ml', 'Ly giữ nhiệt nhôm đen có quai 400ml'),
                    ('Phới Lồng Đánh Trứng Inox Cán Đen', 'Phới lồng đánh trứng inox cán đen'),
                    ('Máy Xay Sinh Tố Cối Thuỷ Tinh 1.5L', 'Máy xay sinh tố cối thuỷ tinh 1.5L'),
                    ('Chảo Xào Sâu Lòng Thép Carbon', 'Chảo xào sâu lòng thép carbon'),
                    ('Thớt Gỗ Tự Nhiên Chữ Nhật Có Rãnh', 'Thớt gỗ tự nhiên chữ nhật có rãnh'),
                    ('Dụng Cụ Vắt Cam Chanh Bằng Tay', 'Dụng cụ vắt cam chanh bằng tay'),
                    ('Dụng Cụ Cắt Trứng Luộc 3 Kiểu', 'Dụng cụ cắt trứng luộc 3 kiểu'), ('Bếp Từ Đôi Âm Kính Cảm Ứng', 'Bếp từ đôi âm kính cảm ứng'),
                    ('Rây Lọc Inox Lưới Mịn Có Cán', 'Rây lọc inox lưới mịn có cán'), ('Nĩa Inox 304 Bộ 6 Chiếc', 'Nĩa inox 304 bộ 6 chiếc'),
                    ('Cốc Thuỷ Tinh Trong Suốt Bộ 6 Ly', 'Cốc thuỷ tinh trong suốt bộ 6 ly'),
                    ('Dụng Cụ Bào Rau Củ 4 Mặt Inox', 'Dụng cụ bào rau củ 4 mặt inox'), ('Máy Xay Cầm Tay Đa Năng', 'Máy xay cầm tay đa năng'),
                    ('Khay Làm Đá Silicon 15 Viên Có Nắp', 'Khay làm đá silicon 15 viên có nắp'), ('Rây Bột Inox Cán Gỗ', 'Rây bột inox cán gỗ'),
                    ('Dao Thái Inox Cán Gỗ 20cm', 'Dao thái inox cán gỗ 20cm'), ('Cặp Lồng Cơm Inox 4 Tầng', 'Cặp lồng cơm inox 4 tầng'),
                    ('Lò Vi Sóng Cơ 20L Màu Trắng', 'Lò vi sóng cơ 20L màu trắng'), ('Cây Treo Cốc Inox 6 Móc', 'Cây treo cốc inox 6 móc'),
                    ('Chảo Chống Dính Đáy Từ', 'Chảo chống dính đáy từ'), ('Đĩa Sứ Trắng Viền Hoạ Tiết Lá', 'Đĩa sứ trắng viền hoạ tiết lá'),
                    ('Kẹp Gắp Thức Ăn Inox Đầu Silicon', 'Kẹp gắp thức ăn inox đầu silicon'), ('Nồi Inox 3 Đáy Nắp Kính', 'Nồi inox 3 đáy nắp kính'),
                    ('Xẻng Lật Có Rãnh Inox', 'Xẻng lật có rãnh inox'), ('Kệ Gia Vị Xoay 6 Lọ Thuỷ Tinh', 'Kệ gia vị xoay 6 lọ thuỷ tinh'),
                    ('Thìa Inox 304 Bộ 6 Chiếc', 'Thìa inox 304 bộ 6 chiếc'), ('Khay Gỗ Phục Vụ Có Quai', 'Khay gỗ phục vụ có quai'),
                    ('Cây Cán Bột Gỗ Kèm Thớt Tròn', 'Cây cán bột gỗ kèm thớt tròn'), ('Dao Bào Vỏ Rau Củ Lưỡi Ngang', 'Dao bào vỏ rau củ lưỡi ngang'),
                    ('Táo Đỏ Nhập Khẩu Giòn Ngọt', 'Táo đỏ nhập khẩu giòn ngọt'), ('Kiwi Vàng Nhập Khẩu', 'Kiwi vàng nhập khẩu'),
                    ('Chanh Vàng Mỹ Mọng Nước', 'Chanh vàng Mỹ mọng nước'), ('Dâu Tằm Đà Lạt Chín Mọng', 'Dâu tằm Đà Lạt chín mọng'),
                    ('Dâu Tây Đà Lạt Hộp 500g', 'Dâu tây Đà Lạt hộp 500g'), ('Dưa Leo Baby Hữu Cơ', 'Dưa leo baby hữu cơ'),
                    ('Ớt Chuông Xanh Đà Lạt', 'Ớt chuông xanh Đà Lạt'), ('Ớt Xanh Cay Tươi', 'Ớt xanh cay tươi'),
                    ('Khoai Tây Đà Lạt Củ To', 'Khoai tây Đà Lạt củ to'), ('Hành Tây Tím Lý Sơn', 'Hành tây tím Lý Sơn'),
                    ('Thăn Bò Úc Cắt Steak 300g', 'Thăn bò Úc cắt steak 300g'), ('Gà Ta Nguyên Con Làm Sạch', 'Gà ta nguyên con làm sạch'),
                    ('Cá Hồi Nauy Phi Lê Cắt Khúc', 'Cá hồi Nauy phi lê cắt khúc'), ('Trứng Gà Ta Hộp 10 Quả', 'Trứng gà ta hộp 10 quả'),
                    ('Sữa Tươi Thanh Trùng Chai 1L', 'Sữa tươi thanh trùng chai 1L'), ('Kem Socola Hộp 450ml', 'Kem socola hộp 450ml'),
                    ('Nước Cam Ép Nguyên Chất Chai 1L', 'Nước cam ép nguyên chất chai 1L'), ('Nước Ngọt Có Ga Chai 1.5L', 'Nước ngọt có ga chai 1.5L'),
                    ('Nước Khoáng Thiên Nhiên Thùng 24 Chai', 'Nước khoáng thiên nhiên thùng 24 chai'),
                    ('Cà Phê Hoà Tan Nescafé Classic Hũ 200g', 'Cà phê hoà tan Nescafé Classic hũ 200g'),
                    ('Gạo ST25 Thơm Dẻo Túi Giấy 2kg', 'Gạo ST25 thơm dẻo túi giấy 2kg'), ('Dầu Ăn Hướng Dương Chai 1L', 'Dầu ăn hướng dương chai 1L'),
                    ('Mật Ong Rừng Nguyên Chất Hũ 500g', 'Mật ong rừng nguyên chất hũ 500g'),
                    ('Khăn Giấy Rút Hộp 3 Lớp Mềm Mịn', 'Khăn giấy rút hộp 3 lớp mềm mịn'),
                    ('Sữa Bột Whey Protein Tăng Cơ Hũ 2.27kg', 'Sữa bột whey protein tăng cơ hũ 2.27kg'),
                    ('Pate Mèo Whiskas Vị Gà Gói 85g', 'Pate mèo Whiskas vị gà gói 85g'),
                    ('Thức Ăn Hạt Cho Chó Vị Cừu Túi 1.5kg', 'Thức ăn hạt cho chó vị cừu túi 1.5kg'),
                    ('Apple MacBook Pro 14 inch Chip M Space Grey', 'Apple MacBook Pro 14 inch chip M Space Grey'),
                    ('Laptop Asus Zenbook Pro Duo Hai Màn Hình', 'Laptop Asus Zenbook Pro Duo hai màn hình'),
                    ('Laptop Huawei MateBook X Pro Màn Hình 3K', 'Laptop Huawei MateBook X Pro màn hình 3K'),
                    ('Laptop Lenovo Yoga 920 Xoay Gập 360 Độ', 'Laptop Lenovo Yoga 920 xoay gập 360 độ'),
                    ('Laptop Dell XPS 13 9300 Viền Mỏng', 'Laptop Dell XPS 13 9300 viền mỏng'),
                    ('Apple iPad Mini 2021 Wifi Starlight', 'Apple iPad mini 2021 wifi Starlight'),
                    ('Samsung Galaxy Tab S8 Plus Kèm Bút S Pen', 'Samsung Galaxy Tab S8 Plus kèm bút S Pen'),
                    ('Samsung Galaxy Tab 10 inch Màu Trắng', 'Samsung Galaxy Tab 10 inch màu trắng'),
                    ('Điện Thoại Apple iPhone 5s Bản Quốc Tế', 'Điện thoại Apple iPhone 5s bản quốc tế'),
                    ('Điện Thoại Apple iPhone 6 Bạc', 'Điện thoại Apple iPhone 6 bạc'),
                    ('Điện Thoại Apple iPhone 13 Pro Xanh Sierra', 'Điện thoại Apple iPhone 13 Pro xanh Sierra'),
                    ('Điện Thoại Apple iPhone X Đen', 'Điện thoại Apple iPhone X đen'),
                    ('Điện Thoại Oppo A57 Vàng Hồng', 'Điện thoại Oppo A57 vàng hồng'),
                    ('Điện Thoại Oppo F19 Pro Plus 5G', 'Điện thoại Oppo F19 Pro Plus 5G'),
                    ('Điện Thoại Oppo K1 Màn Hình Giọt Nước', 'Điện thoại Oppo K1 màn hình giọt nước'),
                    ('Điện Thoại Realme C35 Pin 5000mAh', 'Điện thoại Realme C35 pin 5000mAh'),
                    ('Điện Thoại Realme X Camera Thò Thụt', 'Điện thoại Realme X camera thò thụt'),
                    ('Điện Thoại Realme XT Camera 64MP', 'Điện thoại Realme XT camera 64MP'),
                    ('Điện Thoại Samsung Galaxy S7 Đen Bóng', 'Điện thoại Samsung Galaxy S7 đen bóng'),
                    ('Điện Thoại Samsung Galaxy S8 Màn Hình Vô Cực', 'Điện thoại Samsung Galaxy S8 màn hình vô cực'),
                    ('Điện Thoại Samsung Galaxy S10 Prism Black', 'Điện thoại Samsung Galaxy S10 Prism Black'),
                    ('Điện Thoại Vivo S1 Xanh Ngọc', 'Điện thoại Vivo S1 xanh ngọc'), ('Điện Thoại Vivo V9 Vàng Đồng', 'Điện thoại Vivo V9 vàng đồng'),
                    ('Điện Thoại Vivo X21 Đỏ Cảm Biến Vân Tay Dưới Màn Hình', 'Điện thoại Vivo X21 đỏ cảm biến vân tay dưới màn hình'),
                    ('Đế Sạc Không Dây Apple AirPower', 'Đế sạc không dây Apple AirPower'),
                    ('Bộ Sạc iPhone 20W Kèm Cáp USB-C To Lightning', 'Bộ sạc iPhone 20W kèm cáp USB-C to Lightning'),
                    ('Pin Sạc Dự Phòng Apple MagSafe Battery Pack', 'Pin sạc dự phòng Apple MagSafe Battery Pack'),
                    ('Ốp Lưng Silicon iPhone 12 Có MagSafe', 'Ốp lưng silicon iPhone 12 có MagSafe'),
                    ('Gậy Chụp Ảnh Tripod 3 Chân Kèm Remote Bluetooth', 'Gậy chụp ảnh tripod 3 chân kèm remote Bluetooth'),
                    ('Đèn Livestream Vòng 26cm Kèm Chân Đứng', 'Đèn livestream vòng 26cm kèm chân đứng'),
                    ('Gậy Selfie Cầm Tay Rút Gọn', 'Gậy selfie cầm tay rút gọn'),
                    ('Loa Thông Minh Amazon Echo Plus Thế Hệ 2', 'Loa thông minh Amazon Echo Plus thế hệ 2'),
                    ('Loa Thông Minh Apple HomePod Mini', 'Loa thông minh Apple HomePod mini'),
                    ('Tai Nghe Bluetooth Apple AirPods Thế Hệ 3', 'Tai nghe Bluetooth Apple AirPods thế hệ 3'),
                    ('Tai Nghe Chụp Tai Apple AirPods Max Bạc', 'Tai nghe chụp tai Apple AirPods Max bạc'),
                    ('Tai Nghe Không Dây Beats Flex Đeo Cổ', 'Tai nghe không dây Beats Flex đeo cổ'),
                    ('Đồng Hồ Nam Dây Da Nâu Lịch Tuần Trăng', 'Đồng hồ nam dây da nâu lịch tuần trăng'),
                    ('Đồng Hồ Longines Master Collection Automatic', 'Đồng hồ Longines Master Collection Automatic'),
                    ('Đồng Hồ Rolex Cellini Date Mặt Đen Dây Da', 'Đồng hồ Rolex Cellini Date mặt đen dây da'),
                    ('Đồng Hồ Rolex Cellini Moonphase Vàng Hồng', 'Đồng hồ Rolex Cellini Moonphase vàng hồng'),
                    ('Đồng Hồ Rolex Datejust Demi Vàng 41mm', 'Đồng hồ Rolex Datejust demi vàng 41mm'),
                    ('Đồng Hồ Rolex Submariner Mặt Đen', 'Đồng hồ Rolex Submariner mặt đen'),
                    ('Đồng Hồ IWC Ingenieur Automatic Thép', 'Đồng hồ IWC Ingenieur Automatic thép'),
                    ('Đồng Hồ Nữ Rolex Cellini Moonphase Dây Da', 'Đồng hồ nữ Rolex Cellini Moonphase dây da'),
                    ('Đồng Hồ Nữ Rolex Datejust 31mm Demi', 'Đồng hồ nữ Rolex Datejust 31mm demi'),
                    ('Đồng Hồ Nữ Dây Kim Loại Mạ Vàng Mặt Tròn', 'Đồng hồ nữ dây kim loại mạ vàng mặt tròn'),
                    ('Đồng Hồ Nữ Dây Thép Không Gỉ Mặt Đen Tối Giản', 'Đồng hồ nữ dây thép không gỉ mặt đen tối giản'),
                    ('Đồng Hồ Thông Minh Apple Watch Series 4 Vàng', 'Đồng hồ thông minh Apple Watch Series 4 vàng'),
                    ('Áo Sơ Mi Nam Kẻ Caro Xanh Đen Dài Tay', 'Áo sơ mi nam kẻ caro xanh đen dài tay'),
                    ('Áo Thun Nam Gaming In Hình Gigabyte Aorus', 'Áo thun nam gaming in hình Gigabyte Aorus'),
                    ('Áo Sơ Mi Flannel Nam Caro Đỏ Đen', 'Áo sơ mi flannel nam caro đỏ đen'),
                    ('Áo Sơ Mi Nam Ngắn Tay Hoạ Tiết Hawaii', 'Áo sơ mi nam ngắn tay hoạ tiết Hawaii'),
                    ('Áo Sơ Mi Nam Kẻ Caro Xanh Rêu Form Regular', 'Áo sơ mi nam kẻ caro xanh rêu form regular'),
                    ('Kính Mát Gọng Đồi Mồi Tròng Xanh Rêu Chống UV400', 'Kính mát gọng đồi mồi tròng xanh rêu chống UV400'),
                    ('Kính Mát Phi Công Gọng Kim Loại Tròng Gradient', 'Kính mát phi công gọng kim loại tròng gradient'),
                    ('Kính Mát Phi Công Tròng Xanh Lá', 'Kính mát phi công tròng xanh lá'),
                    ('Kính Pixel Vui Nhộn Đi Tiệc', 'Kính pixel vui nhộn đi tiệc'),
                    ('Kính Mát Mắt Vuông Gọng Trong Suốt', 'Kính mát mắt vuông gọng trong suốt'),
                    ('Đầm Xoè Hoa Nhí Xanh Cổ Tròn', 'Đầm xoè hoa nhí xanh cổ tròn'),
                    ('Đầm Maxi Hai Dây Hoạ Tiết Đi Biển', 'Đầm maxi hai dây hoạ tiết đi biển'),
                    ('Đầm Suông Xám Cổ Yếm Cài Nút', 'Đầm suông xám cổ yếm cài nút'), ('Đầm Xoè Xanh Ngọc Tay Bồng', 'Đầm xoè xanh ngọc tay bồng'),
                    ('Đầm Kẻ Tartan Phối Nơ Đen', 'Đầm kẻ tartan phối nơ đen'),
                    ('Đầm Dạ Hội Đen Dáng Dài Cúp Ngực', 'Đầm dạ hội đen dáng dài cúp ngực'),
                    ('Set Áo Corset Da Đỏ Kèm Chân Váy Dài', 'Set áo corset da đỏ kèm chân váy dài'),
                    ('Set Áo Corset Kèm Chân Váy Đen Dáng Dài', 'Set áo corset kèm chân váy đen dáng dài'),
                    ('Đầm Xoè Chấm Bi Trắng Đen Cổ Điển', 'Đầm xoè chấm bi trắng đen cổ điển'),
                    ('Đầm Suông Đỏ Đô Tay Dài Phối Đen', 'Đầm suông đỏ đô tay dài phối đen'),
                    ('Bông Tai Đá Pha Lê Xanh Lục Dáng Giọt Nước', 'Bông tai đá pha lê xanh lục dáng giọt nước'),
                    ('Bông Tai Tròn Hoạ Tiết Caro Xanh', 'Bông tai tròn hoạ tiết caro xanh'),
                    ('Bông Tai Hình Lá Nhiệt Đới Mạ Vàng', 'Bông tai hình lá nhiệt đới mạ vàng'),
                    ('Giày Nike Air Jordan 1 Đỏ Đen Cổ Cao', 'Giày Nike Air Jordan 1 đỏ đen cổ cao'),
                    ('Giày Puma Future Rider Phối Màu Xanh Vàng', 'Giày Puma Future Rider phối màu xanh vàng'),
                    ('Giày Sneaker Trắng Phối Đỏ Đế Bằng', 'Giày sneaker trắng phối đỏ đế bằng'),
                    ('Giày Sneaker Trắng Gót Đỏ Basic', 'Giày sneaker trắng gót đỏ basic'),
                    ('Giày Cao Gót Calvin Klein Mũi Nhọn Da Đen', 'Giày cao gót Calvin Klein mũi nhọn da đen'),
                    ('Giày Cao Gót Ánh Kim Vàng Gót Nhọn 9cm', 'Giày cao gót ánh kim vàng gót nhọn 9cm'),
                    ('Giày Cao Gót Đen Mũi Nhọn Công Sở 7cm', 'Giày cao gót đen mũi nhọn công sở 7cm'),
                    ('Giày Cao Gót Đỏ Quai Mảnh Dự Tiệc', 'Giày cao gót đỏ quai mảnh dự tiệc'),
                    ('Bốt Nữ Cổ Ngắn Da Phối Đen Nâu', 'Bốt nữ cổ ngắn da phối đen nâu'),
                    ('Túi Xách Tay Nữ Da Xanh Navy Dáng Hộp', 'Túi xách tay nữ da xanh navy dáng hộp'),
                    ('Túi Xách Nữ Heshe Da Bò Nâu Bò', 'Túi xách nữ Heshe da bò nâu bò'),
                    ('Túi Xách Prada Galleria Da Saffiano Xanh Ngọc', 'Túi xách Prada Galleria da Saffiano xanh ngọc'),
                    ('Túi Xách Nữ Đen Có Quai Đeo Chéo', 'Túi xách nữ đen có quai đeo chéo'),
                    ('Balo Nữ Da Thuỷ Trắng Nắp Gập', 'Balo nữ da thuỷ trắng nắp gập'),
                    ('Bóng Bầu Dục Da Tổng Hợp Size Tiêu Chuẩn', 'Bóng bầu dục da tổng hợp size tiêu chuẩn'),
                    ('Bóng Chày Da Khâu Tay Thi Đấu', 'Bóng chày da khâu tay thi đấu'),
                    ('Găng Tay Bắt Bóng Chày Da Thật', 'Găng tay bắt bóng chày da thật'),
                    ('Gậy Bóng Chày Hợp Kim Nhôm 32 inch', 'Gậy bóng chày hợp kim nhôm 32 inch'),
                    ('Giày Đinh Bóng Chày Nike Trắng Xanh', 'Giày đinh bóng chày Nike trắng xanh'),
                    ('Bóng Rổ Da PU Số 7 Trong Nhà Ngoài Trời', 'Bóng rổ da PU số 7 trong nhà ngoài trời'),
                    ('Vành Rổ Kèm Lưới Thép Treo Tường', 'Vành rổ kèm lưới thép treo tường'),
                    ('Bóng Cricket Da Đỏ Khâu Tay', 'Bóng cricket da đỏ khâu tay'), ('Gậy Cricket Gỗ Liễu Anh Quốc', 'Gậy cricket gỗ liễu Anh Quốc'),
                    ('Mũ Bảo Hiểm Cricket Có Lưới Chắn Mặt', 'Mũ bảo hiểm cricket có lưới chắn mặt'),
                    ('Bộ Cọc Wicket Cricket Gỗ 3 Cọc', 'Bộ cọc wicket cricket gỗ 3 cọc'),
                    ('Quả Cầu Lông Lông Vũ Ống 12 Quả', 'Quả cầu lông lông vũ ống 12 quả'),
                    ('Bóng Đá Da PU Khâu Máy Thi Đấu', 'Bóng đá da PU khâu máy thi đấu'),
                    ('Bóng Golf Titleist Pro V1 Hộp 12 Quả', 'Bóng golf Titleist Pro V1 hộp 12 quả'),
                    ('Gậy Golf Sắt Số 7 Thân Thép', 'Gậy golf sắt số 7 thân thép'), ('Bóng Tennis Nỉ Vàng Hộp 3 Quả', 'Bóng tennis nỉ vàng hộp 3 quả'),
                    ('Vợt Tennis Khung Carbon Đỏ 285g', 'Vợt tennis khung carbon đỏ 285g'),
                    ('Bóng Chuyền Da PU Số 5 Thi Đấu', 'Bóng chuyền da PU số 5 thi đấu'),
                    ('Xe Mô Tô Thể Thao 150cc Bạc Đen', 'Xe mô tô thể thao 150cc bạc đen'),
                    ('Xe Mô Tô Kawasaki Z800 Naked Bike', 'Xe mô tô Kawasaki Z800 naked bike'),
                    ('Mô Hình Xe Đua MotoGP Tỉ Lệ 1:12', 'Mô hình xe đua MotoGP tỉ lệ 1:12'),
                    ('Xe Tay Ga 125cc Phanh ABS Màu Trắng', 'Xe tay ga 125cc phanh ABS màu trắng'),
                    ('Xe Mô Tô Sportbike 1000cc Xanh Đen', 'Xe mô tô sportbike 1000cc xanh đen'),
                    ('Bình Sữa Silicone Cho Bé Chống Sặc 150ml', 'Bình sữa silicone cho bé chống sặc 150ml'),
                    ('Xe Đẩy Em Bé Gấp Gọn Có Mái Che Màu Đỏ', 'Xe đẩy em bé gấp gọn có mái che màu đỏ'),
                    ('Ti Giả Silicone Mềm Cho Bé Sơ Sinh Kèm Nắp', 'Ti giả silicone mềm cho bé sơ sinh kèm nắp'),
                    ('Tã Vải Có Túi Chống Thấm Hoạ Tiết Vòng Tròn', 'Tã vải có túi chống thấm hoạ tiết vòng tròn'),
                    ('Ghế Ăn Dặm Chân Gỗ Khay Rời Cho Bé', 'Ghế ăn dặm chân gỗ khay rời cho bé'),
                    ('Cũi Gỗ Thông Sơn Nâu Kèm Nệm Cho Bé', 'Cũi gỗ thông sơn nâu kèm nệm cho bé'),
                    ('Giày Tập Đi Cổ Cao Buộc Dây Cho Bé Xanh Navy', 'Giày tập đi cổ cao buộc dây cho bé xanh navy'),
                    ('Ghế Ngồi Ô Tô Nâng Đệm Có Tựa Lưng Cho Bé', 'Ghế ngồi ô tô nâng đệm có tựa lưng cho bé'),
                    ('Xe Đẩy Ba Trong Một Kèm Nôi Và Ghế Ngồi Ô Tô', 'Xe đẩy ba trong một kèm nôi và ghế ngồi ô tô'),
                    ('Bộ Khối Gỗ Xếp Hình Màu Pastel Cho Bé', 'Bộ khối gỗ xếp hình màu pastel cho bé'),
                    ('Xe Tập Đi Gỗ Kèm Bộ Khối Xếp Hình', 'Xe tập đi gỗ kèm bộ khối xếp hình'),
                    ('Bộ Khối Gỗ Chữ Cái Tiếng Anh Cho Bé', 'Bộ khối gỗ chữ cái tiếng Anh cho bé'),
                    ('Bộ Khối Xốp Ghép Hình Lập Phương Nhiều Màu', 'Bộ khối xốp ghép hình lập phương nhiều màu'),
                    ('Xe Đạp Trẻ Em Màu Đỏ Có Bánh Phụ', 'Xe đạp trẻ em màu đỏ có bánh phụ'),
                    ('Xe Thăng Bằng Khung Gỗ Sồi Bánh Đỏ', 'Xe thăng bằng khung gỗ sồi bánh đỏ'),
                    ('Gấu Bông Teddy Mặc Áo Len Đỏ', 'Gấu bông Teddy mặc áo len đỏ'),
                    ('Gấu Bông Lông Xù Phong Cách Cổ Điển', 'Gấu bông lông xù phong cách cổ điển'),
                    ('Khối Lập Phương Xoay 3x3 Sáu Màu Xoay Trơn', 'Khối lập phương xoay 3x3 sáu màu xoay trơn'),
                    ('Khối Lập Phương Xoay 2x2 Cỡ Nhỏ Cho Bé', 'Khối lập phương xoay 2x2 cỡ nhỏ cho bé'),
                    ('Xe Ô Tô Mô Hình Kim Loại Thể Thao Màu Cam', 'Xe ô tô mô hình kim loại thể thao màu cam'),
                    ('Xe Đua Mô Hình Cổ Điển Màu Xanh', 'Xe đua mô hình cổ điển màu xanh'),
                    ('Máy Ảnh Mirrorless Sony Alpha Full Frame Kèm Ống Kính', 'Máy ảnh mirrorless Sony Alpha full frame kèm ống kính'),
                    ('Máy Ảnh Mirrorless Canon EOS R10 Thân Máy', 'Máy ảnh mirrorless Canon EOS R10 thân máy'),
                    ('Máy Ảnh DSLR Nikon D5300 Kèm Ống Kính Zoom 18-140mm', 'Máy ảnh DSLR Nikon D5300 kèm ống kính zoom 18-140mm'),
                    ('Máy Ảnh DSLR Pentax K-7 Kèm Ống Kính Kit', 'Máy ảnh DSLR Pentax K-7 kèm ống kính kit'),
                    ('Máy Ảnh DSLR Canon EOS 80D Kèm Ống Kính 18-135mm', 'Máy ảnh DSLR Canon EOS 80D kèm ống kính 18-135mm'),
                    ('Máy Ảnh Compact Nikon Coolpix Zoom Quang 10x Màu Đỏ', 'Máy ảnh compact Nikon Coolpix zoom quang 10x màu đỏ'),
                    ('Máy Ảnh Compact Panasonic Lumix Cảm Biến Lớn Ống Kính Leica', 'Máy ảnh compact Panasonic Lumix cảm biến lớn ống kính Leica'),
                    ('Chân Máy Ảnh Carbon Gập Gọn Du Lịch Bốn Khúc Chân', 'Chân máy ảnh carbon gập gọn du lịch bốn khúc chân'),
                    ('Chân Máy Bạch Tuộc Uốn Dẻo Cho Máy Ảnh Nhỏ', 'Chân máy bạch tuộc uốn dẻo cho máy ảnh nhỏ'),
                    ('Ống Kính Nikon AF 50mm f/1.4 Chụp Chân Dung', 'Ống kính Nikon AF 50mm f/1.4 chụp chân dung'),
                    ('Ống Kính Canon RF 50mm f/1.2L Khẩu Lớn Cao Cấp', 'Ống kính Canon RF 50mm f/1.2L khẩu lớn cao cấp'),
                    ('Thẻ Nhớ SDHC Transcend 8GB Cho Máy Ảnh', 'Thẻ nhớ SDHC Transcend 8GB cho máy ảnh'),
                    ('Máy Quay Phim Sony Handycam Ổ Cứng Zoom Quang Học', 'Máy quay phim Sony Handycam ổ cứng zoom quang học'),
                    ('Máy Quay Chuyên Nghiệp Panasonic Cầm Tay Kèm Micro', 'Máy quay chuyên nghiệp Panasonic cầm tay kèm micro'),
                    ('Hạt Khô Cho Mèo Trưởng Thành Vị Cá Ngừ Túi 1.5kg', 'Hạt khô cho mèo trưởng thành vị cá ngừ túi 1.5kg'),
                    ('Bánh Thưởng Nhân Kem Cho Mèo Vị Gà Gói 60g', 'Bánh thưởng nhân kem cho mèo vị gà gói 60g'),
                    ('Hạt Khô Cho Chó Trưởng Thành Vị Bò Túi 3kg', 'Hạt khô cho chó trưởng thành vị bò túi 3kg'),
                    ('Xương Gặm Da Bò Thắt Nút Làm Sạch Răng Cho Chó', 'Xương gặm da bò thắt nút làm sạch răng cho chó'),
                    ('Bát Ăn Chậm Chống Nuốt Nhanh Cho Chó Mèo Màu Xanh Lá', 'Bát ăn chậm chống nuốt nhanh cho chó mèo màu xanh lá'),
                    ('Vòng Cổ Thắt Nơ Kẻ Caro Cho Chó Mèo', 'Vòng cổ thắt nơ kẻ caro cho chó mèo'),
                    ('Vòng Cổ Đính Hoa Vải Hoạ Tiết Ong Cho Chó Mèo', 'Vòng cổ đính hoa vải hoạ tiết ong cho chó mèo'),
                    ('Giường Sofa Có Bánh Xe Kèm Gối Cho Chó Mèo', 'Giường sofa có bánh xe kèm gối cho chó mèo'),
                    ('Áo Thun Nam Dài Tay In Hình Thuyền Buồm Trắng', 'Áo thun nam dài tay in hình thuyền buồm trắng'),
                    ('Áo Thun Nam Xanh Rêu In Chữ Đối Xứng', 'Áo thun nam xanh rêu in chữ đối xứng'),
                    ('Áo Polo Nam Đen Basic Thêu Ngực', 'Áo polo nam đen basic thêu ngực'),
                    ('Áo Sơ Mi Nam Kẻ Caro Madras Dài Tay', 'Áo sơ mi nam kẻ caro Madras dài tay'),
                    ('Quần Jean Nam Ống Đứng Xanh Đậm Chỉ Vàng', 'Quần jean nam ống đứng xanh đậm chỉ vàng'),
                    ('Quần Kaki Nam Xám Nhạt Dáng Slim', 'Quần kaki nam xám nhạt dáng slim'),
                    ('Quần Kaki Nam Xanh Navy Ống Đứng Công Sở', 'Quần kaki nam xanh navy ống đứng công sở'),
                    ('Quần Short Jean Nam Xanh Wash Ngang Gối', 'Quần short jean nam xanh wash ngang gối'),
                    ('Quần Short Kaki Nam Vàng Nhạt Đi Biển', 'Quần short kaki nam vàng nhạt đi biển'),
                    ('Áo Thun Nữ Cổ Tròn Hồng Pastel Basic', 'Áo thun nữ cổ tròn hồng pastel basic'),
                    ('Quần Jean Nữ Skinny Đen Lưng Cao Nâng Mông', 'Quần jean nữ skinny đen lưng cao nâng mông'),
                    ('Quần Ống Loe Nữ Vải Ánh Kim Vàng Champagne', 'Quần ống loe nữ vải ánh kim vàng champagne'),
                    ('Viên Uống Selen Và Vitamin E Tăng Đề Kháng Lọ 90 Viên', 'Viên uống selen và vitamin E tăng đề kháng lọ 90 viên'),
                    ('Viên Dầu Cá Omega-3 1000mg Hộp 120 Viên Nang Mềm', 'Viên dầu cá Omega-3 1000mg hộp 120 viên nang mềm'),
                    ('Máy Đo Huyết Áp Bắp Tay Microlife Màn Hình Lớn', 'Máy đo huyết áp bắp tay Microlife màn hình lớn'),
                    ('Máy Đo Huyết Áp Cổ Tay Điện Tử Tự Động', 'Máy đo huyết áp cổ tay điện tử tự động'),
                    ('Bộ Đo Huyết Áp Cơ Đồng Hồ Kim Kèm Bóp Hơi', 'Bộ đo huyết áp cơ đồng hồ kim kèm bóp hơi'),
                    ('Nhiệt Kế Điện Tử Đầu Mềm Đo Nhanh Chống Nước', 'Nhiệt kế điện tử đầu mềm đo nhanh chống nước'),
                    ('Nhiệt Kế Hồng Ngoại Đo Tai Và Trán Cho Cả Gia Đình', 'Nhiệt kế hồng ngoại đo tai và trán cho cả gia đình'),
                    ('Máy Đo Nồng Độ Oxy Máu SpO2 Kẹp Đầu Ngón Tay', 'Máy đo nồng độ oxy máu SpO2 kẹp đầu ngón tay'),
                    ('Khẩu Trang Y Tế 4 Lớp Kháng Khuẩn Hộp 50 Cái', 'Khẩu trang y tế 4 lớp kháng khuẩn hộp 50 cái'),
                    ('Áo Khoác Bomber Nam Đỏ Có Túi Tay', 'Áo khoác bomber nam đỏ có túi tay'),
                    ('Áo Khoác Phao Nam Vàng Mù Tạt Có Mũ Trùm', 'Áo khoác phao nam vàng mù tạt có mũ trùm'),
                    ('Áo Khoác Nỉ Nam Xanh Navy Cổ Đứng Khoá Kéo', 'Áo khoác nỉ nam xanh navy cổ đứng khoá kéo'),
                    ('Áo Khoác Kaki Nữ Màu Be Tay Ngắn Cúc Đồng', 'Áo khoác kaki nữ màu be tay ngắn cúc đồng'),
                    ('Áo Thun Nữ Đen In Hoạ Tiết Vẽ Tay', 'Áo thun nữ đen in hoạ tiết vẽ tay'),
                    ('Quần Jean Nữ Ống Suông Wash Nhạt Cạp Vừa', 'Quần jean nữ ống suông wash nhạt cạp vừa'),
                    ('Giày Tây Nam Oxford Da Bò Màu Bò Mũi Trơn', 'Giày tây nam Oxford da bò màu bò mũi trơn'),
                    ('Giày Tây Nam Brogue Da Nâu Đục Lỗ Hoạ Tiết', 'Giày tây nam brogue da nâu đục lỗ hoạ tiết'),
                    ('Giày Tây Nam Oxford Da Đen Bóng Công Sở', 'Giày tây nam Oxford da đen bóng công sở'),
                    ('Giày Lười Nam Penny Loafer Da Bò Màu Bò', 'Giày lười nam penny loafer da bò màu bò'),
                    ('Giày Lười Nam Da Lộn Nâu Chuông Tua Rua', 'Giày lười nam da lộn nâu chuông tua rua'),
                    ('Dép Xỏ Ngón Nam Đế Cao Su Trắng Quai Đen', 'Dép xỏ ngón nam đế cao su trắng quai đen'),
                    ('Giày Chạy Bộ Nam Có Đèn LED Ở Gót Chạy Đêm', 'Giày chạy bộ nam có đèn LED ở gót chạy đêm'),
                    ('Bốt Oxford Nữ Cổ Thấp Lót Lông Thủ Công', 'Bốt Oxford nữ cổ thấp lót lông thủ công'),
                    ('Giày Loafer Nữ Da Bóng Nâu Đỏ Gót Vuông', 'Giày loafer nữ da bóng nâu đỏ gót vuông'),
                    ('Dép Xỏ Ngón Nữ Đế Trắng Quai Nâu Nhạt', 'Dép xỏ ngón nữ đế trắng quai nâu nhạt'),
                    ('Sandal Nữ Da Bò Quai Hậu Kiểu Menorca', 'Sandal nữ da bò quai hậu kiểu Menorca'),
                    ('Sandal Đế Xuồng Nữ Quai Ngang Xanh Navy', 'Sandal đế xuồng nữ quai ngang xanh navy'),
                    ('Giày Búp Bê Nữ Trắng Mũi Phối Màu Đế Xanh', 'Giày búp bê nữ trắng mũi phối màu đế xanh'),
                    ('Giày Búp Bê Nữ Kẻ Caro Đỏ Đính Nơ', 'Giày búp bê nữ kẻ caro đỏ đính nơ'),
                    ('Giày Bệt Nữ Da Đỏ Mũi Nhọn Đính Nơ', 'Giày bệt nữ da đỏ mũi nhọn đính nơ'),
                    ('Túi Đeo Chéo Nữ Da Bê Mềm Màu Nâu Kem', 'Túi đeo chéo nữ da bê mềm màu nâu kem'),
                    ('Ví Cầm Tay Nữ Da Đen Kèm Dây Đeo Chéo', 'Ví cầm tay nữ da đen kèm dây đeo chéo'),
                    ('Túi Đeo Chéo Vải Canvas In Hoạ Tiết Cành Hoa', 'Túi đeo chéo vải canvas in hoạ tiết cành hoa'),
                    ('Túi Dự Tiệc Nữ Da Ánh Bạc Quai Xích', 'Túi dự tiệc nữ da ánh bạc quai xích'),
                    ('Túi Rút Dây Móc Len Đen Tua Rua Thủ Công', 'Túi rút dây móc len đen tua rua thủ công'),
                    ('Ví Dài Nữ Da Màu Be Nhiều Ngăn Thẻ', 'Ví dài nữ da màu be nhiều ngăn thẻ'),
                    ('Túi Xách Nữ Da Vân Cá Sấu Nâu Bò Khoá Cài', 'Túi xách nữ da vân cá sấu nâu bò khoá cài'),
                    ('Điện Thoại Nokia 1280 Phổ Thông Pin Lâu Có Đèn Pin', 'Điện thoại Nokia 1280 phổ thông pin lâu có đèn pin'),
                    ('Điện Thoại Nokia 8210 4G Màn Hình Lớn Xanh Đậm', 'Điện thoại Nokia 8210 4G màn hình lớn xanh đậm'),
                    ('Điện Thoại Nokia 3310 Cổ Điển Xanh Dương', 'Điện thoại Nokia 3310 cổ điển xanh dương'),
                    ('Điện Thoại Xiaomi Mi Note Pro Trắng Viền Vàng', 'Điện thoại Xiaomi Mi Note Pro trắng viền vàng'),
                    ('Điện Thoại Xiaomi Redmi Note 15 Đen Camera Kép', 'Điện thoại Xiaomi Redmi Note 15 đen camera kép'),
                    ('Tai Nghe Bluetooth Sony Đeo Cổ Extra Bass', 'Tai nghe Bluetooth Sony đeo cổ Extra Bass'),
                    ('Tai Nghe Chụp Tai Audio-Technica Kiểm Âm Có Dây', 'Tai nghe chụp tai Audio-Technica kiểm âm có dây'),
                    ('Tai Nghe Gaming Chụp Tai Có Micro Đỏ Đen', 'Tai nghe gaming chụp tai có micro đỏ đen'),
                    ('Tai Nghe True Wireless Hộp Sạc Nhôm Xám', 'Tai nghe true wireless hộp sạc nhôm xám'),
                    ('Loa Bluetooth Xách Tay Brionvega Phong Cách Cổ Điển', 'Loa Bluetooth xách tay Brionvega phong cách cổ điển'),
                    ('Loa Bluetooth Mini Polk Nhỏ Gọn Âm Trầm Mạnh', 'Loa Bluetooth mini Polk nhỏ gọn âm trầm mạnh'),
                    ('Vòng Đeo Tay Thông Minh Fitbit Theo Dõi Sức Khoẻ', 'Vòng đeo tay thông minh Fitbit theo dõi sức khoẻ'),
                    ('Áo Thun Nam Xanh Nhạt Cổ Tròn In Hoạ Tiết Nhỏ', 'Áo thun nam xanh nhạt cổ tròn in hoạ tiết nhỏ'),
                    ('Áo Thun Thể Thao Nam Xanh Đen Hoạ Tiết Tia Chớp', 'Áo thun thể thao nam xanh đen hoạ tiết tia chớp'),
                    ('Áo Thun Nam Trơn Xám Than Cotton Dày Dặn', 'Áo thun nam trơn xám than cotton dày dặn'),
                    ('Áo Thun Nam Đen Thêu Hoạ Tiết Chữ Thập Trắng', 'Áo thun nam đen thêu hoạ tiết chữ thập trắng'),
                    ('Camera Hành Trình Ô Tô Màn Hình Kép Quay Trước Sau', 'Camera hành trình ô tô màn hình kép quay trước sau'),
                    ('Áo Mưa Poncho Vải Dù Rằn Ri Có Mũ Trùm', 'Áo mưa poncho vải dù rằn ri có mũ trùm'),
                    ('Ấm Siêu Tốc Thuỷ Tinh Xanh Có Đèn Báo 1.7L', 'Ấm siêu tốc thuỷ tinh xanh có đèn báo 1.7L'),
                    ('Ấm Đun Nước Siêu Tốc Russell Hobbs Vạch Nước Xanh', 'Ấm đun nước siêu tốc Russell Hobbs vạch nước xanh'),
                    ('Ấm Siêu Tốc Bosch Thân Kính Đế Xoay 1.7L', 'Ấm siêu tốc Bosch thân kính đế xoay 1.7L'),
                    ('Nồi Cơm Điện Nắp Rời Lòng Inox Màu Bạc', 'Nồi cơm điện nắp rời lòng inox màu bạc'),
                    ('Nồi Cơm Điện Tử Panasonic Màn Hình Cảm Ứng 1L', 'Nồi cơm điện tử Panasonic màn hình cảm ứng 1L'),
                    ('Nồi Hấp Điện Đa Năng Tatung Xanh Cốm Cổ Điển', 'Nồi hấp điện đa năng Tatung xanh cốm cổ điển'),
                    ('Mũ Bảo Hiểm Fullface Trắng Kính Chắn Gió Trong Suốt', 'Mũ bảo hiểm fullface trắng kính chắn gió trong suốt'),
                    ('Mũ Bảo Hiểm Lật Hàm Đen Bóng Kính Khói', 'Mũ bảo hiểm lật hàm đen bóng kính khói'),
                    ('Son Thỏi Hồng Đất Chất Kem Mềm Môi', 'Son thỏi hồng đất chất kem mềm môi'),
                    ('Bộ Chảo Gốm Chống Dính Nhiều Màu Đáy Từ', 'Bộ chảo gốm chống dính nhiều màu đáy từ'),
                    ('Chảo Chống Dính Đen Cán Nhựa Cách Nhiệt', 'Chảo chống dính đen cán nhựa cách nhiệt');
                CREATE TEMP TABLE l202_w (old_v text, new_v text);
                INSERT INTO l202_w VALUES
                    ('Chính Hãng', 'Chính hãng'), ('Bảo Hành 12 Tháng', 'Bảo hành 12 tháng'), ('Nguyên Seal', 'Nguyên seal'),
                    ('Đã Qua Sử Dụng 99%', 'Đã qua sử dụng 99%'), ('Bản Quốc Tế', 'Bản quốc tế'), ('Hàng Trưng Bày 99%', 'Hàng trưng bày 99%'),
                    ('Fullbox', 'Fullbox'), ('Đã Qua Sử Dụng', 'Đã qua sử dụng'), ('Bảo Hành 2 Năm', 'Bảo hành 2 năm'), ('Form Rộng', 'Form rộng'),
                    ('Vải Mềm Mát', 'Vải mềm mát'), ('Phong Cách Hàn Quốc', 'Phong cách Hàn Quốc'), ('Hàng Thiết Kế', 'Hàng thiết kế'),
                    ('Mẫu Mới', 'Mẫu mới'), ('Đế Êm', 'Đế êm'), ('Full Size', 'Full size'), ('Hàng Có Sẵn', 'Hàng có sẵn'), ('Cao Cấp', 'Cao cấp'),
                    ('Bền Đẹp', 'Bền đẹp'), ('Giao Nhanh', 'Giao nhanh'), ('Date Mới', 'Date mới'), ('Chất Lượng Cao', 'Chất lượng cao'),
                    ('Giao Toàn Quốc', 'Giao toàn quốc'), ('Giá Tốt', 'Giá tốt'), ('Hàng Mới Về', 'Hàng mới về'), ('An Toàn Cho Bé', 'An toàn cho bé'),
                    ('Quà Tặng Cho Bé', 'Quà tặng cho bé'), ('Đã Qua Sử Dụng 98%', 'Đã qua sử dụng 98%');
                CREATE TEMP TABLE l202_t (old_v text, new_v text);
                INSERT INTO l202_t VALUES
                    ('Dung Tích', 'Dung tích'), ('Kích Thước', 'Kích thước'), ('Khối Lượng', 'Khối lượng'), ('Cấu Hình', 'Cấu hình'),
                    ('Dung Lượng', 'Dung lượng'), ('Màu Sắc', 'Màu sắc'), ('Kích Cỡ', 'Kích cỡ');
                CREATE TEMP TABLE l202_o (old_v text, new_v text);
                INSERT INTO l202_o VALUES
                    ('Tím Mận', 'Tím mận'), ('Xanh Navy', 'Xanh navy'), ('Xanh Đen', 'Xanh đen'), ('Đỏ Đen', 'Đỏ đen'), ('Xanh Rêu', 'Xanh rêu'),
                    ('Xanh Ngọc', 'Xanh ngọc'), ('Trắng Đen', 'Trắng đen'), ('Xanh Vàng', 'Xanh vàng'), ('Đen Nâu', 'Đen nâu'),
                    ('Trắng Xanh', 'Trắng xanh'), ('Xanh Dương', 'Xanh dương'), ('Gói 1 Tã', 'Gói 1 tã'), ('Combo 3 Tã', 'Combo 3 tã'),
                    ('Xanh Lá', 'Xanh lá'), ('Nâu Đỏ', 'Nâu đỏ');

                CREATE TEMP TABLE l202_hit AS
                SELECT DISTINCT ON (p.id) p.id, m.old_v AS old_model, m.new_v AS new_model,
                       m.new_v || CASE WHEN length(p.name) = length(m.old_v) THEN ''
                                       ELSE ' - ' || coalesce(w.new_v, substr(p.name, length(m.old_v) + 4)) END AS new_name
                FROM catalog.products p
                JOIN l202_m m ON left(p.name, length(m.old_v)) = m.old_v
                              AND (length(p.name) = length(m.old_v) OR substr(p.name, length(m.old_v) + 1, 3) = ' - ')
                LEFT JOIN l202_w w ON substr(p.name, length(m.old_v) + 4) = w.old_v
                ORDER BY p.id, length(m.old_v) DESC;

                UPDATE catalog.products p
                SET name = h.new_name,
                    description = replace(p.description, '<strong>' || h.old_model || '</strong>', '<strong>' || h.new_model || '</strong>'),
                    updated_at = now()
                FROM l202_hit h WHERE p.id = h.id;

                UPDATE catalog.variant_tiers vt SET name = t.new_v
                FROM l202_t t, l202_hit h WHERE vt.product_id = h.id AND vt.name = t.old_v;

                UPDATE catalog.variant_options vo SET value = o.new_v
                FROM l202_o o, catalog.variant_tiers vt, l202_hit h
                WHERE vo.tier_id = vt.id AND vt.product_id = h.id AND vo.value = o.old_v;

                -- The description's variant line ("<li>Dung Tích: 50ml / 100ml</li>"): one pair at a time, a row takes one
                -- update per statement
                DO $l202$
                DECLARE r record;
                BEGIN
                    FOR r IN SELECT '<li>' || old_v || ':' AS old_v, '<li>' || new_v || ':' AS new_v FROM l202_t
                             UNION ALL SELECT old_v, new_v FROM l202_o LOOP
                        UPDATE catalog.products p SET description = replace(p.description, r.old_v, r.new_v)
                        FROM l202_hit h WHERE p.id = h.id AND strpos(p.description, r.old_v) > 0;
                    END LOOP;
                END $l202$;

                -- The search index holds the names: re-sync the renamed products (200 a message)
                INSERT INTO sys.outbox_messages (id, type, payload, occurred_at, attempts)
                SELECT gen_random_uuid(), 'search.sync.products', jsonb_build_object('productIds', jsonb_agg(id)), now(), 0
                FROM (SELECT id, (row_number() OVER (ORDER BY id) - 1) / 200 AS chunk FROM l202_hit) g
                GROUP BY chunk;

                DROP TABLE l202_m, l202_w, l202_t, l202_o, l202_hit;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The Title Case names were a defect of the sample data: nothing to restore
        }
    }
}

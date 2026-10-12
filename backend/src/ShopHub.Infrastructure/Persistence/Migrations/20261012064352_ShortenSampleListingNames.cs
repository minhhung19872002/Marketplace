using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShortenSampleListingNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G-VIS round 5 (L209): seven sample listings in ten were named "model - shop wording" ("… - Chính hãng", "… - Date
            // mới"); real listings mostly carry the item's own name. The seeder now names about one listing in five that way
            // (ProductGenerator.PlainName). Here the wording goes from every listing still named so by the seed (a model name
            // of the sample data, " - ", one of its wordings) unless the shop already uses the shortened name — a shop
            // listing one item twice keeps the wording that tells them apart (about one listing in four on the sample data).
            // Second-hand wordings ("Đã qua sử dụng 99%") say the condition and stay. A name a seller changed matches no
            // pair. The slug loses the same words; the product page keeps its id-based URL. Renamed products are queued for
            // search.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE l209_m (name text PRIMARY KEY);
                INSERT INTO l209_m VALUES
                    ('Apple MacBook Pro 14 inch chip M Space Grey'), ('Apple iPad mini 2021 wifi Starlight'), ('Balo nữ da thuỷ trắng nắp gập'),
                    ('Bánh thưởng nhân kem cho mèo vị gà gói 60g'), ('Bát ăn chậm chống nuốt nhanh cho chó mèo màu xanh lá'),
                    ('Bình sữa silicone cho bé chống sặc 150ml'), ('Bóng bầu dục da tổng hợp size tiêu chuẩn'), ('Bóng chuyền da PU số 5 thi đấu'),
                    ('Bóng chày da khâu tay thi đấu'), ('Bóng cricket da đỏ khâu tay'), ('Bóng golf Titleist Pro V1 hộp 12 quả'),
                    ('Bóng rổ da PU số 7 trong nhà ngoài trời'), ('Bóng tennis nỉ vàng hộp 3 quả'), ('Bóng đá da PU khâu máy thi đấu'),
                    ('Bông tai hình lá nhiệt đới mạ vàng'), ('Bông tai tròn hoạ tiết caro xanh'), ('Bông tai đá pha lê xanh lục dáng giọt nước'),
                    ('Bảng phấn mắt 12 ô kèm gương'), ('Bếp từ đôi âm kính cảm ứng'), ('Bốt Oxford nữ cổ thấp lót lông thủ công'),
                    ('Bốt nữ cổ ngắn da phối đen nâu'), ('Bộ chảo gốm chống dính nhiều màu đáy từ'), ('Bộ cọc wicket cricket gỗ 3 cọc'),
                    ('Bộ khối gỗ chữ cái tiếng Anh cho bé'), ('Bộ khối gỗ xếp hình màu pastel cho bé'), ('Bộ khối xốp ghép hình lập phương nhiều màu'),
                    ('Bộ sạc iPhone 20W kèm cáp USB-C to Lightning'), ('Bộ đo huyết áp cơ đồng hồ kim kèm bóp hơi'),
                    ('Camera hành trình ô tô màn hình kép quay trước sau'), ('Chanh vàng Mỹ mọng nước'),
                    ('Chân máy bạch tuộc uốn dẻo cho máy ảnh nhỏ'), ('Chân máy ảnh carbon gập gọn du lịch bốn khúc chân'),
                    ('Chảo chống dính đen cán nhựa cách nhiệt'), ('Chảo chống dính đáy từ'), ('Chảo xào sâu lòng thép carbon'),
                    ('Chậu cây xanh mini trang trí phòng khách'), ('Cà phê hoà tan Nescafé Classic hũ 200g'), ('Cá hồi Nauy phi lê cắt khúc'),
                    ('Cây cán bột gỗ kèm thớt tròn'), ('Cây cảnh để bàn chậu đứng kim loại'), ('Cây treo cốc inox 6 móc'),
                    ('Cũi gỗ thông sơn nâu kèm nệm cho bé'), ('Cặp lồng cơm inox 4 tầng'), ('Cốc thuỷ tinh trong suốt bộ 6 ly'),
                    ('Dao bào vỏ rau củ lưỡi ngang'), ('Dao thái inox cán gỗ 20cm'), ('Dâu tây Đà Lạt hộp 500g'), ('Dâu tằm Đà Lạt chín mọng'),
                    ('Dép xỏ ngón nam đế cao su trắng quai đen'), ('Dép xỏ ngón nữ đế trắng quai nâu nhạt'), ('Dưa leo baby hữu cơ'),
                    ('Dầu ăn hướng dương chai 1L'), ('Dụng cụ bào rau củ 4 mặt inox'), ('Dụng cụ cắt trứng luộc 3 kiểu'),
                    ('Dụng cụ vắt cam chanh bằng tay'), ('Ghế ngồi ô tô nâng đệm có tựa lưng cho bé'), ('Ghế xoay văn phòng bọc nỉ chân nhôm'),
                    ('Ghế xích đu mây treo ban công kèm nệm'), ('Ghế ăn dặm chân gỗ khay rời cho bé'), ('Giày Nike Air Jordan 1 đỏ đen cổ cao'),
                    ('Giày Puma Future Rider phối màu xanh vàng'), ('Giày búp bê nữ kẻ caro đỏ đính nơ'),
                    ('Giày búp bê nữ trắng mũi phối màu đế xanh'), ('Giày bệt nữ da đỏ mũi nhọn đính nơ'),
                    ('Giày cao gót Calvin Klein mũi nhọn da đen'), ('Giày cao gót ánh kim vàng gót nhọn 9cm'),
                    ('Giày cao gót đen mũi nhọn công sở 7cm'), ('Giày cao gót đỏ quai mảnh dự tiệc'), ('Giày chạy bộ nam có đèn LED ở gót chạy đêm'),
                    ('Giày loafer nữ da bóng nâu đỏ gót vuông'), ('Giày lười nam da lộn nâu chuông tua rua'),
                    ('Giày lười nam penny loafer da bò màu bò'), ('Giày sneaker trắng gót đỏ basic'), ('Giày sneaker trắng phối đỏ đế bằng'),
                    ('Giày tây nam Oxford da bò màu bò mũi trơn'), ('Giày tây nam Oxford da đen bóng công sở'),
                    ('Giày tây nam brogue da nâu đục lỗ hoạ tiết'), ('Giày tập đi cổ cao buộc dây cho bé xanh navy'),
                    ('Giày đinh bóng chày Nike trắng xanh'), ('Giường ngủ gỗ tự nhiên đầu giường bọc nệm 1m8'),
                    ('Giường sofa có bánh xe kèm gối cho chó mèo'), ('Gà ta nguyên con làm sạch'), ('Găng tay bắt bóng chày da thật'),
                    ('Gạo ST25 thơm dẻo túi giấy 2kg'), ('Gấu bông Teddy mặc áo len đỏ'), ('Gấu bông lông xù phong cách cổ điển'),
                    ('Gậy bóng chày hợp kim nhôm 32 inch'), ('Gậy chụp ảnh tripod 3 chân kèm remote Bluetooth'), ('Gậy cricket gỗ liễu Anh Quốc'),
                    ('Gậy golf sắt số 7 thân thép'), ('Gậy selfie cầm tay rút gọn'), ('Hành tây tím Lý Sơn'),
                    ('Hạt khô cho chó trưởng thành vị bò túi 3kg'), ('Hạt khô cho mèo trưởng thành vị cá ngừ túi 1.5kg'), ('Kem socola hộp 450ml'),
                    ('Khay gỗ phục vụ có quai'), ('Khay làm đá silicon 15 viên có nắp'), ('Khoai tây Đà Lạt củ to'),
                    ('Khung ảnh treo tường hình cây gia đình 12 ô'), ('Khăn giấy rút hộp 3 lớp mềm mịn'),
                    ('Khẩu trang y tế 4 lớp kháng khuẩn hộp 50 cái'), ('Khối lập phương xoay 2x2 cỡ nhỏ cho bé'),
                    ('Khối lập phương xoay 3x3 sáu màu xoay trơn'), ('Kiwi vàng nhập khẩu'), ('Kính mát gọng đồi mồi tròng xanh rêu chống UV400'),
                    ('Kính mát mắt vuông gọng trong suốt'), ('Kính mát phi công gọng kim loại tròng gradient'), ('Kính mát phi công tròng xanh lá'),
                    ('Kính pixel vui nhộn đi tiệc'), ('Kẹp gắp thức ăn inox đầu silicon'), ('Kệ gia vị xoay 6 lọ thuỷ tinh'),
                    ('Laptop Asus Zenbook Pro Duo hai màn hình'), ('Laptop Dell XPS 13 9300 viền mỏng'), ('Laptop Huawei MateBook X Pro màn hình 3K'),
                    ('Laptop Lenovo Yoga 920 xoay gập 360 độ'), ('Loa Bluetooth mini Polk nhỏ gọn âm trầm mạnh'),
                    ('Loa Bluetooth xách tay Brionvega phong cách cổ điển'), ('Loa thông minh Amazon Echo Plus thế hệ 2'),
                    ('Loa thông minh Apple HomePod mini'), ('Ly giữ nhiệt nhôm đen có quai 400ml'), ('Lò vi sóng cơ 20L màu trắng'),
                    ('Mascara Essence Lash Princess làm dày và cong mi'), ('Máy quay chuyên nghiệp Panasonic cầm tay kèm micro'),
                    ('Máy quay phim Sony Handycam ổ cứng zoom quang học'), ('Máy xay cầm tay đa năng'), ('Máy xay sinh tố cối thuỷ tinh 1.5L'),
                    ('Máy đo huyết áp bắp tay Microlife màn hình lớn'), ('Máy đo huyết áp cổ tay điện tử tự động'),
                    ('Máy đo nồng độ oxy máu SpO2 kẹp đầu ngón tay'), ('Máy ảnh DSLR Canon EOS 80D kèm ống kính 18-135mm'),
                    ('Máy ảnh DSLR Nikon D5300 kèm ống kính zoom 18-140mm'), ('Máy ảnh DSLR Pentax K-7 kèm ống kính kit'),
                    ('Máy ảnh compact Nikon Coolpix zoom quang 10x màu đỏ'), ('Máy ảnh compact Panasonic Lumix cảm biến lớn ống kính Leica'),
                    ('Máy ảnh mirrorless Canon EOS R10 thân máy'), ('Máy ảnh mirrorless Sony Alpha full frame kèm ống kính'),
                    ('Mô hình xe đua MotoGP tỉ lệ 1:12'), ('Mũ bảo hiểm cricket có lưới chắn mặt'),
                    ('Mũ bảo hiểm fullface trắng kính chắn gió trong suốt'), ('Mũ bảo hiểm lật hàm đen bóng kính khói'),
                    ('Mật ong rừng nguyên chất hũ 500g'), ('Nhiệt kế hồng ngoại đo tai và trán cho cả gia đình'),
                    ('Nhiệt kế điện tử đầu mềm đo nhanh chống nước'), ('Nĩa inox 304 bộ 6 chiếc'), ('Nước cam ép nguyên chất chai 1L'),
                    ('Nước hoa Calvin Klein CK One Eau de Toilette'), ('Nước hoa Chanel Coco Noir Eau de Parfum'),
                    ('Nước hoa Dior J''adore Eau de Parfum'), ('Nước hoa Dolce & Gabbana Dolce Shine'), ('Nước hoa Gucci Bloom Eau de Parfum'),
                    ('Nước khoáng thiên nhiên thùng 24 chai'), ('Nước ngọt có ga chai 1.5L'), ('Nước rửa tay Attitude Super Leaves hương chanh'),
                    ('Nồi cơm điện nắp rời lòng inox màu bạc'), ('Nồi cơm điện tử Panasonic màn hình cảm ứng 1L'),
                    ('Nồi hấp điện đa năng Tatung xanh cốm cổ điển'), ('Nồi inox 3 đáy nắp kính'), ('Pate mèo Whiskas vị gà gói 85g'),
                    ('Phấn phủ nén kiềm dầu mịn nhẹ'), ('Phới lồng đánh trứng inox cán đen'), ('Pin sạc dự phòng Apple MagSafe Battery Pack'),
                    ('Quả cầu lông lông vũ ống 12 quả'), ('Quần jean nam ống đứng xanh đậm chỉ vàng'), ('Quần jean nữ skinny đen lưng cao nâng mông'),
                    ('Quần jean nữ ống suông wash nhạt cạp vừa'), ('Quần kaki nam xanh navy ống đứng công sở'), ('Quần kaki nam xám nhạt dáng slim'),
                    ('Quần short jean nam xanh wash ngang gối'), ('Quần short kaki nam vàng nhạt đi biển'),
                    ('Quần ống loe nữ vải ánh kim vàng champagne'), ('Rây bột inox cán gỗ'), ('Rây lọc inox lưới mịn có cán'),
                    ('Samsung Galaxy Tab 10 inch màu trắng'), ('Samsung Galaxy Tab S8 Plus kèm bút S Pen'), ('Sandal nữ da bò quai hậu kiểu Menorca'),
                    ('Sandal đế xuồng nữ quai ngang xanh navy'), ('Set áo corset da đỏ kèm chân váy dài'), ('Set áo corset kèm chân váy đen dáng dài'),
                    ('Sofa văng vải bố 3 chỗ phong cách Ý'), ('Son thỏi hồng đất chất kem mềm môi'), ('Son thỏi lì màu đỏ cổ điển'),
                    ('Sơn móng tay đỏ bóng lâu trôi'), ('Sữa bột whey protein tăng cơ hũ 2.27kg'), ('Sữa dưỡng thể Vaseline Men cho da mặt và body'),
                    ('Sữa tươi thanh trùng chai 1L'), ('Sữa tắm Olay Ultra Moisture bơ hạt mỡ 700ml'), ('Tai nghe Bluetooth Apple AirPods thế hệ 3'),
                    ('Tai nghe Bluetooth Sony đeo cổ Extra Bass'), ('Tai nghe chụp tai Apple AirPods Max bạc'),
                    ('Tai nghe chụp tai Audio-Technica kiểm âm có dây'), ('Tai nghe gaming chụp tai có micro đỏ đen'),
                    ('Tai nghe không dây Beats Flex đeo cổ'), ('Tai nghe true wireless hộp sạc nhôm xám'), ('Thìa inox 304 bộ 6 chiếc'),
                    ('Thăn bò Úc cắt steak 300g'), ('Thẻ nhớ SDHC Transcend 8GB cho máy ảnh'), ('Thớt gỗ tự nhiên chữ nhật có rãnh'),
                    ('Thức ăn hạt cho chó vị cừu túi 1.5kg'), ('Ti giả silicone mềm cho bé sơ sinh kèm nắp'), ('Trứng gà ta hộp 10 quả'),
                    ('Táo đỏ nhập khẩu giòn ngọt'), ('Tã vải có túi chống thấm hoạ tiết vòng tròn'), ('Túi dự tiệc nữ da ánh bạc quai xích'),
                    ('Túi rút dây móc len đen tua rua thủ công'), ('Túi xách Prada Galleria da Saffiano xanh ngọc'),
                    ('Túi xách nữ Heshe da bò nâu bò'), ('Túi xách nữ da vân cá sấu nâu bò khoá cài'), ('Túi xách nữ đen có quai đeo chéo'),
                    ('Túi xách tay nữ da xanh navy dáng hộp'), ('Túi đeo chéo nữ da bê mềm màu nâu kem'),
                    ('Túi đeo chéo vải canvas in hoạ tiết cành hoa'), ('Tủ lavabo gỗ kèm gương phòng tắm'), ('Tủ đầu giường gỗ gụ 2 ngăn kéo'),
                    ('Viên dầu cá Omega-3 1000mg hộp 120 viên nang mềm'), ('Viên uống selen và vitamin E tăng đề kháng lọ 90 viên'),
                    ('Vành rổ kèm lưới thép treo tường'), ('Ví cầm tay nữ da đen kèm dây đeo chéo'), ('Ví dài nữ da màu be nhiều ngăn thẻ'),
                    ('Vòng cổ thắt nơ kẻ caro cho chó mèo'), ('Vòng cổ đính hoa vải hoạ tiết ong cho chó mèo'),
                    ('Vòng đeo tay thông minh Fitbit theo dõi sức khoẻ'), ('Vợt tennis khung carbon đỏ 285g'), ('Xe mô tô Kawasaki Z800 naked bike'),
                    ('Xe mô tô sportbike 1000cc xanh đen'), ('Xe mô tô thể thao 150cc bạc đen'), ('Xe tay ga 125cc phanh ABS màu trắng'),
                    ('Xe thăng bằng khung gỗ sồi bánh đỏ'), ('Xe tập đi gỗ kèm bộ khối xếp hình'), ('Xe ô tô mô hình kim loại thể thao màu cam'),
                    ('Xe đua mô hình cổ điển màu xanh'), ('Xe đạp trẻ em màu đỏ có bánh phụ'), ('Xe đẩy ba trong một kèm nôi và ghế ngồi ô tô'),
                    ('Xe đẩy em bé gấp gọn có mái che màu đỏ'), ('Xương gặm da bò thắt nút làm sạch răng cho chó'), ('Xẻng lật có rãnh inox'),
                    ('Xẻng xào gỗ tre chịu nhiệt'), ('Áo khoác bomber nam đỏ có túi tay'), ('Áo khoác kaki nữ màu be tay ngắn cúc đồng'),
                    ('Áo khoác nỉ nam xanh navy cổ đứng khoá kéo'), ('Áo khoác phao nam vàng mù tạt có mũ trùm'),
                    ('Áo mưa poncho vải dù rằn ri có mũ trùm'), ('Áo polo nam đen basic thêu ngực'), ('Áo sơ mi flannel nam caro đỏ đen'),
                    ('Áo sơ mi nam kẻ caro Madras dài tay'), ('Áo sơ mi nam kẻ caro xanh rêu form regular'), ('Áo sơ mi nam kẻ caro xanh đen dài tay'),
                    ('Áo sơ mi nam ngắn tay hoạ tiết Hawaii'), ('Áo thun nam dài tay in hình thuyền buồm trắng'),
                    ('Áo thun nam gaming in hình Gigabyte Aorus'), ('Áo thun nam trơn xám than cotton dày dặn'),
                    ('Áo thun nam xanh nhạt cổ tròn in hoạ tiết nhỏ'), ('Áo thun nam xanh rêu in chữ đối xứng'),
                    ('Áo thun nam đen thêu hoạ tiết chữ thập trắng'), ('Áo thun nữ cổ tròn hồng pastel basic'), ('Áo thun nữ đen in hoạ tiết vẽ tay'),
                    ('Áo thun thể thao nam xanh đen hoạ tiết tia chớp'), ('Điện thoại Apple iPhone 13 Pro xanh Sierra'),
                    ('Điện thoại Apple iPhone 5s bản quốc tế'), ('Điện thoại Apple iPhone 6 bạc'), ('Điện thoại Apple iPhone X đen'),
                    ('Điện thoại Nokia 1280 phổ thông pin lâu có đèn pin'), ('Điện thoại Nokia 3310 cổ điển xanh dương'),
                    ('Điện thoại Nokia 8210 4G màn hình lớn xanh đậm'), ('Điện thoại Oppo A57 vàng hồng'), ('Điện thoại Oppo F19 Pro Plus 5G'),
                    ('Điện thoại Oppo K1 màn hình giọt nước'), ('Điện thoại Realme C35 pin 5000mAh'), ('Điện thoại Realme X camera thò thụt'),
                    ('Điện thoại Realme XT camera 64MP'), ('Điện thoại Samsung Galaxy S10 Prism Black'), ('Điện thoại Samsung Galaxy S7 đen bóng'),
                    ('Điện thoại Samsung Galaxy S8 màn hình vô cực'), ('Điện thoại Vivo S1 xanh ngọc'), ('Điện thoại Vivo V9 vàng đồng'),
                    ('Điện thoại Vivo X21 đỏ cảm biến vân tay dưới màn hình'), ('Điện thoại Xiaomi Mi Note Pro trắng viền vàng'),
                    ('Điện thoại Xiaomi Redmi Note 15 đen camera kép'), ('Đèn bàn chụp vải chân gỗ cổ điển'),
                    ('Đèn livestream vòng 26cm kèm chân đứng'), ('Đĩa sứ trắng viền hoạ tiết lá'), ('Đầm dạ hội đen dáng dài cúp ngực'),
                    ('Đầm kẻ tartan phối nơ đen'), ('Đầm maxi hai dây hoạ tiết đi biển'), ('Đầm suông xám cổ yếm cài nút'),
                    ('Đầm suông đỏ đô tay dài phối đen'), ('Đầm xoè chấm bi trắng đen cổ điển'), ('Đầm xoè hoa nhí xanh cổ tròn'),
                    ('Đầm xoè xanh ngọc tay bồng'), ('Đế sạc không dây Apple AirPower'), ('Đồng hồ IWC Ingenieur Automatic thép'),
                    ('Đồng hồ Longines Master Collection Automatic'), ('Đồng hồ Rolex Cellini Date mặt đen dây da'),
                    ('Đồng hồ Rolex Cellini Moonphase vàng hồng'), ('Đồng hồ Rolex Datejust demi vàng 41mm'), ('Đồng hồ Rolex Submariner mặt đen'),
                    ('Đồng hồ nam dây da nâu lịch tuần trăng'), ('Đồng hồ nữ Rolex Cellini Moonphase dây da'), ('Đồng hồ nữ Rolex Datejust 31mm demi'),
                    ('Đồng hồ nữ dây kim loại mạ vàng mặt tròn'), ('Đồng hồ nữ dây thép không gỉ mặt đen tối giản'),
                    ('Đồng hồ thông minh Apple Watch Series 4 vàng'), ('Ấm siêu tốc Bosch thân kính đế xoay 1.7L'),
                    ('Ấm siêu tốc thuỷ tinh xanh có đèn báo 1.7L'), ('Ấm đun nước siêu tốc Russell Hobbs vạch nước xanh'),
                    ('Ống kính Canon RF 50mm f/1.2L khẩu lớn cao cấp'), ('Ống kính Nikon AF 50mm f/1.4 chụp chân dung'),
                    ('Ốp lưng silicon iPhone 12 có MagSafe'), ('Ớt chuông xanh Đà Lạt'), ('Ớt xanh cay tươi');
                CREATE TEMP TABLE l209_w (wording text, slug text);
                INSERT INTO l209_w VALUES
                    ('Chính hãng', 'chinh-hang'), ('Bảo hành 12 tháng', 'bao-hanh-12-thang'), ('Nguyên seal', 'nguyen-seal'),
                    ('Bản quốc tế', 'ban-quoc-te'), ('Fullbox', 'fullbox'), ('Bảo hành 2 năm', 'bao-hanh-2-nam'), ('Form rộng', 'form-rong'),
                    ('Vải mềm mát', 'vai-mem-mat'), ('Phong cách Hàn Quốc', 'phong-cach-han-quoc'), ('Hàng thiết kế', 'hang-thiet-ke'),
                    ('Mẫu mới', 'mau-moi'), ('Đế êm', 'de-em'), ('Full size', 'full-size'), ('Hàng có sẵn', 'hang-co-san'), ('Cao cấp', 'cao-cap'),
                    ('Bền đẹp', 'ben-dep'), ('Giao nhanh', 'giao-nhanh'), ('Date mới', 'date-moi'), ('Chất lượng cao', 'chat-luong-cao'),
                    ('Giao toàn quốc', 'giao-toan-quoc'), ('Giá tốt', 'gia-tot'), ('Hàng mới về', 'hang-moi-ve'), ('An toàn cho bé', 'an-toan-cho-be'),
                    ('Quà tặng cho bé', 'qua-tang-cho-be');

                CREATE TEMP TABLE l209_hit AS
                SELECT p.id, p.shop_id, m.name AS new_name,
                       CASE WHEN right(p.slug, length(w.slug) + 1) = '-' || w.slug THEN left(p.slug, length(p.slug) - length(w.slug) - 1)
                            ELSE p.slug END AS new_slug,
                       row_number() OVER (PARTITION BY p.shop_id, m.name ORDER BY p.id) AS n
                FROM catalog.products p
                JOIN l209_w w ON right(p.name, length(w.wording) + 3) = ' - ' || w.wording
                JOIN l209_m m ON m.name = left(p.name, length(p.name) - length(w.wording) - 3)
                WHERE p.deleted_at IS NULL;

                DELETE FROM l209_hit h
                WHERE h.n > 1 OR EXISTS (SELECT 1 FROM catalog.products o
                                         WHERE o.shop_id = h.shop_id AND o.name = h.new_name AND o.id <> h.id AND o.deleted_at IS NULL);

                UPDATE catalog.products p SET name = h.new_name, slug = h.new_slug, updated_at = now()
                FROM l209_hit h WHERE p.id = h.id;

                -- The search index holds the names: re-sync the renamed products (200 a message)
                INSERT INTO sys.outbox_messages (id, type, payload, occurred_at, attempts)
                SELECT gen_random_uuid(), 'search.sync.products', jsonb_build_object('productIds', jsonb_agg(id)), now(), 0
                FROM (SELECT id, (row_number() OVER (ORDER BY id) - 1) / 200 AS chunk FROM l209_hit) g
                GROUP BY chunk;

                DROP TABLE l209_m, l209_w, l209_hit;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

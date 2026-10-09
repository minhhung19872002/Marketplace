# Sample catalogue: every model of MODELS is one real studio photo set (dummyjson.com product id) placed in the leaf
# category it shows, with a natural Vietnamese name, a list price in VND, a variant scheme that fits the item and the industry
# attributes it needs. build.py turns this into Seed/Data/product-models.json + Seed/Data/ProductImages/.
#
# Fields: id (dummyjson), leaf ("Top/Mid/Leaf"), name, price (VND), variant (key of VARIANTS or None), attrs (name →
# values), brand (optional, a verified brand of the seed), origin.

VARIANTS = {
    'phone': ('Dung Lượng', [('64GB', 0), ('128GB', 2_000_000), ('256GB', 4_500_000)]),
    'laptop': ('Cấu Hình', [('8GB/256GB', 0), ('16GB/512GB', 5_000_000)]),
    'tablet': ('Dung Lượng', [('64GB', 0), ('256GB', 3_500_000)]),
    'shirt': ('Kích Cỡ', [('S', 0), ('M', 0), ('L', 0), ('XL', 20_000)]),
    'dress': ('Kích Cỡ', [('S', 0), ('M', 0), ('L', 0)]),
    'men_shoes': ('Size', [('39', 0), ('40', 0), ('41', 0), ('42', 0), ('43', 0)]),
    'women_shoes': ('Size', [('35', 0), ('36', 0), ('37', 0), ('38', 0), ('39', 0)]),
    'perfume': ('Dung Tích', [('50ml', 0), ('100ml', 1_200_000)]),
    'ball5': ('Kích Cỡ', [('Số 4', 0), ('Số 5', 30_000)]),
    'weight1kg': ('Khối Lượng', [('500g', 0), ('1kg', None)]),  # None = double price minus 5%
    'pot': ('Kích Thước', [('20cm', 0), ('24cm', 60_000), ('28cm', 120_000)]),
    'case_color': ('Màu Sắc', [('Tím Mận', 0), ('Đen', 0), ('Xanh Navy', 0)]),
}

F = 'Thời Trang Nam'
W = 'Thời Trang Nữ'
P = 'Điện Thoại & Phụ Kiện'
C = 'Máy Tính & Laptop'
E = 'Thiết Bị Điện Tử'
H = 'Đồng Hồ'
GN = 'Giày Dép Nam'
GW = 'Giày Dép Nữ'
B = 'Túi Ví Nữ'
N = 'Nhà Cửa & Đời Sống'
S = 'Sắc Đẹp'
K = 'Sức Khỏe'
T = 'Thể Thao & Du Lịch'
X = 'Ô Tô & Xe Máy'
G = 'Bách Hóa Online'
PET = 'Thú Cưng'

def m(id, leaf, name, price, variant=None, attrs=None, origin='Việt Nam', brand=None, weight=500):
    return dict(id=id, leaf=leaf, name=name, price=price, variant=variant, attrs=attrs or {}, origin=origin, brand=brand, weight=weight)

MODELS = [
    # ---- Sắc đẹp ----
    m(1, f'{S}/Trang Điểm/Trang Điểm Mắt', 'Mascara Essence Lash Princess Làm Dày Và Cong Mi', 89_000, attrs={'Loại da phù hợp': ['Mọi loại da']}, origin='Khác', weight=50),
    m(2, f'{S}/Trang Điểm/Trang Điểm Mắt', 'Bảng Phấn Mắt 12 Ô Kèm Gương', 159_000, attrs={'Loại da phù hợp': ['Mọi loại da']}, origin='Hàn Quốc', weight=150),
    m(3, f'{S}/Trang Điểm/Kem Nền', 'Phấn Phủ Nén Kiềm Dầu Mịn Nhẹ', 145_000, attrs={'Loại da phù hợp': ['Da dầu', 'Da hỗn hợp']}, origin='Hàn Quốc', weight=80),
    m(4, f'{S}/Trang Điểm/Son Môi', 'Son Thỏi Lì Màu Đỏ Cổ Điển', 129_000, attrs={'Loại da phù hợp': ['Mọi loại da']}, origin='Hàn Quốc', weight=40),
    m(5, f'{S}/Trang Điểm/Sơn Móng Tay', 'Sơn Móng Tay Đỏ Bóng Lâu Trôi', 49_000, origin='Thái Lan', weight=40),
    m(6, f'{S}/Nước Hoa/Nước Hoa Nam', 'Nước Hoa Calvin Klein CK One Eau De Toilette', 1_350_000, 'perfume', origin='Mỹ', weight=300),
    m(7, f'{S}/Nước Hoa/Nước Hoa Nữ', 'Nước Hoa Chanel Coco Noir Eau De Parfum', 3_950_000, 'perfume', origin='Khác', weight=300),
    m(8, f'{S}/Nước Hoa/Nước Hoa Nữ', "Nước Hoa Dior J'adore Eau De Parfum", 3_450_000, 'perfume', origin='Khác', weight=300),
    m(9, f'{S}/Nước Hoa/Nước Hoa Nữ', 'Nước Hoa Dolce & Gabbana Dolce Shine', 2_150_000, 'perfume', origin='Khác', weight=300),
    m(10, f'{S}/Nước Hoa/Nước Hoa Nữ', 'Nước Hoa Gucci Bloom Eau De Parfum', 3_250_000, 'perfume', origin='Khác', weight=300),
    m(118, f'{S}/Chăm Sóc Cơ Thể/Sữa Tắm & Xà Phòng', 'Nước Rửa Tay Attitude Super Leaves Hương Chanh', 189_000, origin='Khác', weight=500),
    m(119, f'{S}/Chăm Sóc Cơ Thể/Sữa Tắm & Xà Phòng', 'Sữa Tắm Olay Ultra Moisture Bơ Hạt Mỡ 700ml', 235_000, origin='Mỹ', weight=800),
    m(120, f'{S}/Chăm Sóc Cơ Thể/Dưỡng Thể', 'Sữa Dưỡng Thể Vaseline Men Cho Da Mặt Và Body', 159_000, origin='Mỹ', weight=450),

    # ---- Nhà cửa ----
    m(11, f'{N}/Nội Thất/Giường & Sofa', 'Giường Ngủ Gỗ Tự Nhiên Đầu Giường Bọc Nệm 1m8', 18_900_000, attrs={'Chất liệu': ['Gỗ']}, weight=30_000),
    m(12, f'{N}/Nội Thất/Giường & Sofa', 'Sofa Văng Vải Bố 3 Chỗ Phong Cách Ý', 12_500_000, attrs={'Chất liệu': ['Khác']}, weight=40_000),
    m(13, f'{N}/Nội Thất/Bàn Ghế', 'Tủ Đầu Giường Gỗ Gụ 2 Ngăn Kéo', 2_350_000, attrs={'Chất liệu': ['Gỗ']}, weight=12_000),
    m(14, f'{N}/Nội Thất/Bàn Ghế', 'Ghế Xoay Văn Phòng Bọc Nỉ Chân Nhôm', 3_150_000, attrs={'Chất liệu': ['Khác']}, weight=9_000),
    m(15, f'{N}/Nội Thất/Phòng Tắm', 'Tủ Lavabo Gỗ Kèm Gương Phòng Tắm', 5_900_000, attrs={'Chất liệu': ['Gỗ']}, weight=25_000),
    m(43, f'{N}/Trang Trí/Đồ Trang Trí', 'Ghế Xích Đu Mây Treo Ban Công Kèm Nệm', 3_290_000, attrs={'Chất liệu': ['Khác']}, weight=15_000),
    m(44, f'{N}/Trang Trí/Đồ Trang Trí', 'Khung Ảnh Treo Tường Hình Cây Gia Đình 12 Ô', 289_000, attrs={'Chất liệu': ['Gỗ']}, weight=1_500),
    m(45, f'{N}/Trang Trí/Cây & Chậu Cảnh', 'Cây Cảnh Để Bàn Chậu Đứng Kim Loại', 359_000, attrs={'Chất liệu': ['Khác']}, weight=2_000),
    m(46, f'{N}/Trang Trí/Cây & Chậu Cảnh', 'Chậu Cây Xanh Mini Trang Trí Phòng Khách', 199_000, attrs={'Chất liệu': ['Gốm sứ']}, weight=1_200),
    m(47, f'{N}/Đèn/Đèn Bàn', 'Đèn Bàn Chụp Vải Chân Gỗ Cổ Điển', 459_000, attrs={'Chất liệu': ['Gỗ']}, weight=1_800),
    m(48, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Xẻng Xào Gỗ Tre Chịu Nhiệt', 35_000, attrs={'Chất liệu': ['Gỗ']}, weight=100),
    m(49, f'{N}/Đồ Dùng Nhà Bếp/Bình Giữ Nhiệt', 'Ly Giữ Nhiệt Nhôm Đen Có Quai 400ml', 149_000, attrs={'Chất liệu': ['Inox']}, weight=300),
    m(50, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Phới Lồng Đánh Trứng Inox Cán Đen', 39_000, attrs={'Chất liệu': ['Inox']}, weight=120),
    m(51, f'{N}/Đồ Dùng Nhà Bếp/Máy Xay', 'Máy Xay Sinh Tố Cối Thuỷ Tinh 1.5L', 890_000, attrs={'Chất liệu': ['Thuỷ tinh'], 'Công suất': ['600']}, weight=3_500),
    m(52, f'{N}/Đồ Dùng Nhà Bếp/Nồi & Chảo', 'Chảo Xào Sâu Lòng Thép Carbon', 329_000, 'pot', attrs={'Chất liệu': ['Khác']}, weight=1_400),
    m(53, f'{N}/Đồ Dùng Nhà Bếp/Dao & Thớt', 'Thớt Gỗ Tự Nhiên Chữ Nhật Có Rãnh', 159_000, attrs={'Chất liệu': ['Gỗ']}, weight=1_200),
    m(54, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Dụng Cụ Vắt Cam Chanh Bằng Tay', 45_000, attrs={'Chất liệu': ['Nhựa']}, weight=200),
    m(55, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Dụng Cụ Cắt Trứng Luộc 3 Kiểu', 55_000, attrs={'Chất liệu': ['Nhựa']}, weight=200),
    m(56, f'{N}/Đồ Dùng Nhà Bếp/Thiết Bị Bếp Điện', 'Bếp Từ Đôi Âm Kính Cảm Ứng', 4_290_000, attrs={'Chất liệu': ['Khác'], 'Công suất': ['4000']}, weight=12_000),
    m(57, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Rây Lọc Inox Lưới Mịn Có Cán', 49_000, attrs={'Chất liệu': ['Inox']}, weight=150),
    m(58, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Nĩa Inox 304 Bộ 6 Chiếc', 69_000, attrs={'Chất liệu': ['Inox']}, weight=300),
    m(59, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Cốc Thuỷ Tinh Trong Suốt Bộ 6 Ly', 119_000, attrs={'Chất liệu': ['Thuỷ tinh']}, weight=1_800),
    m(60, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Dụng Cụ Bào Rau Củ 4 Mặt Inox', 79_000, attrs={'Chất liệu': ['Inox']}, weight=400),
    m(61, f'{N}/Đồ Dùng Nhà Bếp/Máy Xay', 'Máy Xay Cầm Tay Đa Năng', 450_000, attrs={'Chất liệu': ['Nhựa'], 'Công suất': ['400']}, weight=1_200),
    m(62, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Khay Làm Đá Silicon 15 Viên Có Nắp', 39_000, attrs={'Chất liệu': ['Cao su']}, weight=150),
    m(63, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Rây Bột Inox Cán Gỗ', 45_000, attrs={'Chất liệu': ['Inox']}, weight=150),
    m(64, f'{N}/Đồ Dùng Nhà Bếp/Dao & Thớt', 'Dao Thái Inox Cán Gỗ 20cm', 139_000, attrs={'Chất liệu': ['Inox']}, weight=250),
    m(65, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Cặp Lồng Cơm Inox 4 Tầng', 259_000, attrs={'Chất liệu': ['Inox']}, weight=1_400),
    m(66, f'{N}/Đồ Dùng Nhà Bếp/Thiết Bị Bếp Điện', 'Lò Vi Sóng Cơ 20L Màu Trắng', 1_690_000, attrs={'Chất liệu': ['Khác'], 'Công suất': ['700']}, weight=11_000),
    m(67, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Cây Treo Cốc Inox 6 Móc', 89_000, attrs={'Chất liệu': ['Inox']}, weight=500),
    m(68, f'{N}/Đồ Dùng Nhà Bếp/Nồi & Chảo', 'Chảo Chống Dính Đáy Từ', 259_000, 'pot', attrs={'Chất liệu': ['Khác']}, weight=1_000),
    m(69, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Đĩa Sứ Trắng Viền Hoạ Tiết Lá', 59_000, attrs={'Chất liệu': ['Gốm sứ']}, weight=600),
    m(70, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Kẹp Gắp Thức Ăn Inox Đầu Silicon', 45_000, attrs={'Chất liệu': ['Inox']}, weight=150),
    m(71, f'{N}/Đồ Dùng Nhà Bếp/Nồi & Chảo', 'Nồi Inox 3 Đáy Nắp Kính', 389_000, 'pot', attrs={'Chất liệu': ['Inox']}, weight=1_800),
    m(72, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Xẻng Lật Có Rãnh Inox', 49_000, attrs={'Chất liệu': ['Inox']}, weight=150),
    m(73, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Kệ Gia Vị Xoay 6 Lọ Thuỷ Tinh', 229_000, attrs={'Chất liệu': ['Thuỷ tinh']}, weight=1_500),
    m(74, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Thìa Inox 304 Bộ 6 Chiếc', 65_000, attrs={'Chất liệu': ['Inox']}, weight=300),
    m(75, f'{N}/Đồ Dùng Nhà Bếp/Bát Đĩa & Dao Dĩa', 'Khay Gỗ Phục Vụ Có Quai', 189_000, attrs={'Chất liệu': ['Gỗ']}, weight=900),
    m(76, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Cây Cán Bột Gỗ Kèm Thớt Tròn', 99_000, attrs={'Chất liệu': ['Gỗ']}, weight=700),
    m(77, f'{N}/Đồ Dùng Nhà Bếp/Dụng Cụ Nấu Ăn', 'Dao Bào Vỏ Rau Củ Lưỡi Ngang', 29_000, attrs={'Chất liệu': ['Nhựa']}, weight=60),

    # ---- Bách hoá ----
    m(16, f'{G}/Thực Phẩm Tươi/Trái Cây', 'Táo Đỏ Nhập Khẩu Giòn Ngọt', 89_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['14']}, origin='Mỹ', weight=1_000),
    m(30, f'{G}/Thực Phẩm Tươi/Trái Cây', 'Kiwi Vàng Nhập Khẩu', 129_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['10']}, origin='Khác', weight=1_000),
    m(31, f'{G}/Thực Phẩm Tươi/Trái Cây', 'Chanh Vàng Mỹ Mọng Nước', 69_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['14']}, origin='Mỹ', weight=1_000),
    m(33, f'{G}/Thực Phẩm Tươi/Trái Cây', 'Dâu Tằm Đà Lạt Chín Mọng', 75_000, attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['3']}, weight=600),
    m(40, f'{G}/Thực Phẩm Tươi/Trái Cây', 'Dâu Tây Đà Lạt Hộp 500g', 119_000, attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['4']}, weight=600),
    m(21, f'{G}/Thực Phẩm Tươi/Rau Củ', 'Dưa Leo Baby Hữu Cơ', 35_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['5']}, weight=1_000),
    m(25, f'{G}/Thực Phẩm Tươi/Rau Củ', 'Ớt Chuông Xanh Đà Lạt', 39_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['7']}, weight=1_000),
    m(26, f'{G}/Thực Phẩm Tươi/Rau Củ', 'Ớt Xanh Cay Tươi', 25_000, attrs={'Khối lượng': ['200'], 'Hạn sử dụng': ['7']}, weight=300),
    m(35, f'{G}/Thực Phẩm Tươi/Rau Củ', 'Khoai Tây Đà Lạt Củ To', 32_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['20']}, weight=1_000),
    m(37, f'{G}/Thực Phẩm Tươi/Rau Củ', 'Hành Tây Tím Lý Sơn', 29_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['30']}, weight=1_000),
    m(17, f'{G}/Thực Phẩm Tươi/Thịt & Hải Sản', 'Thăn Bò Úc Cắt Steak 300g', 289_000, attrs={'Khối lượng': ['300'], 'Hạn sử dụng': ['5']}, origin='Khác', weight=400),
    m(19, f'{G}/Thực Phẩm Tươi/Thịt & Hải Sản', 'Gà Ta Nguyên Con Làm Sạch', 189_000, attrs={'Khối lượng': ['1500'], 'Hạn sử dụng': ['3']}, weight=1_600),
    m(24, f'{G}/Thực Phẩm Tươi/Thịt & Hải Sản', 'Cá Hồi Nauy Phi Lê Cắt Khúc', 219_000, attrs={'Khối lượng': ['300'], 'Hạn sử dụng': ['3']}, origin='Khác', weight=400),
    m(23, f'{G}/Thực Phẩm Tươi/Trứng & Sữa', 'Trứng Gà Ta Hộp 10 Quả', 45_000, attrs={'Khối lượng': ['600'], 'Hạn sử dụng': ['20']}, weight=700),
    m(32, f'{G}/Thực Phẩm Tươi/Trứng & Sữa', 'Sữa Tươi Thanh Trùng Chai 1L', 39_000, attrs={'Khối lượng': ['1000'], 'Hạn sử dụng': ['10']}, weight=1_100),
    m(28, f'{G}/Đồ Uống/Kem & Đồ Ngọt', 'Kem Socola Hộp 450ml', 99_000, attrs={'Khối lượng': ['450'], 'Hạn sử dụng': ['180']}, weight=500),
    m(29, f'{G}/Đồ Uống/Nước Giải Khát', 'Nước Cam Ép Nguyên Chất Chai 1L', 59_000, attrs={'Khối lượng': ['1000'], 'Hạn sử dụng': ['60']}, weight=1_100),
    m(39, f'{G}/Đồ Uống/Nước Giải Khát', 'Nước Ngọt Có Ga Chai 1.5L', 22_000, attrs={'Khối lượng': ['1500'], 'Hạn sử dụng': ['180']}, weight=1_600),
    m(42, f'{G}/Đồ Uống/Nước Giải Khát', 'Nước Khoáng Thiên Nhiên Thùng 24 Chai', 119_000, attrs={'Khối lượng': ['12000'], 'Hạn sử dụng': ['365']}, weight=12_500),
    m(34, f'{G}/Đồ Uống/Cà Phê', 'Cà Phê Hoà Tan Nescafé Classic Hũ 200g', 155_000, attrs={'Khối lượng': ['200'], 'Hạn sử dụng': ['540']}, origin='Khác', weight=300),
    m(38, f'{G}/Thực Phẩm Khô/Gạo', 'Gạo ST25 Thơm Dẻo Túi Giấy 2kg', 85_000, attrs={'Khối lượng': ['2000'], 'Hạn sử dụng': ['365']}, weight=2_100),
    m(20, f'{G}/Thực Phẩm Khô/Dầu Ăn & Gia Vị', 'Dầu Ăn Hướng Dương Chai 1L', 65_000, attrs={'Khối lượng': ['1000'], 'Hạn sử dụng': ['365']}, weight=1_000),
    m(27, f'{G}/Thực Phẩm Khô/Dầu Ăn & Gia Vị', 'Mật Ong Rừng Nguyên Chất Hũ 500g', 179_000, attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['730']}, weight=700),
    m(41, f'{G}/Đồ Dùng Gia Đình/Khăn Giấy', 'Khăn Giấy Rút Hộp 3 Lớp Mềm Mịn', 29_000, attrs={'Khối lượng': ['300'], 'Hạn sử dụng': ['1095']}, weight=350),
    m(36, f'{K}/Thực Phẩm Chức Năng/Whey Protein', 'Sữa Bột Whey Protein Tăng Cơ Hũ 2.27kg', 1_790_000, attrs={'Hạn sử dụng': ['540']}, origin='Mỹ', weight=2_500),
    m(18, f'{PET}/Thức Ăn Thú Cưng/Thức Ăn Cho Mèo', 'Pate Mèo Whiskas Vị Gà Gói 85g', 15_000, attrs={'Khối lượng': ['85'], 'Hạn sử dụng': ['540']}, origin='Thái Lan', weight=100),
    m(22, f'{PET}/Thức Ăn Thú Cưng/Thức Ăn Cho Chó', 'Thức Ăn Hạt Cho Chó Vị Cừu Túi 1.5kg', 259_000, attrs={'Khối lượng': ['1500'], 'Hạn sử dụng': ['365']}, origin='Khác', weight=1_600),

    # ---- Điện tử ----
    m(78, f'{C}/Laptop/Laptop Văn Phòng', 'Apple MacBook Pro 14 inch Chip M Space Grey', 39_990_000, 'laptop', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Mỹ', weight=1_600),
    m(79, f'{C}/Laptop/Laptop Văn Phòng', 'Laptop Asus Zenbook Pro Duo Hai Màn Hình', 45_990_000, 'laptop', attrs={'Bảo hành': ['24 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Khác', weight=2_300),
    m(80, f'{C}/Laptop/Laptop Văn Phòng', 'Laptop Huawei MateBook X Pro Màn Hình 3K', 32_990_000, 'laptop', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=1_400),
    m(81, f'{C}/Laptop/Laptop Văn Phòng', 'Laptop Lenovo Yoga 920 Xoay Gập 360 Độ', 21_490_000, 'laptop', attrs={'Bảo hành': ['24 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=1_400),
    m(82, f'{C}/Laptop/Laptop Văn Phòng', 'Laptop Dell XPS 13 9300 Viền Mỏng', 28_990_000, 'laptop', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Mỹ', weight=1_300),
    m(159, f'{C}/Máy Tính Bảng/iPad & Tablet', 'Apple iPad Mini 2021 Wifi Starlight', 12_490_000, 'tablet', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'USB-C']}, origin='Mỹ', weight=600),
    m(160, f'{C}/Máy Tính Bảng/iPad & Tablet', 'Samsung Galaxy Tab S8 Plus Kèm Bút S Pen', 18_990_000, 'tablet', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Hàn Quốc', weight=800),
    m(161, f'{C}/Máy Tính Bảng/iPad & Tablet', 'Samsung Galaxy Tab 10 inch Màu Trắng', 6_490_000, 'tablet', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Hàn Quốc', weight=700),
    m(121, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Apple iPhone 5s Bản Quốc Tế', 1_490_000, 'phone', attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'Lightning']}, origin='Mỹ', weight=300),
    m(122, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Apple iPhone 6 Bạc', 2_190_000, 'phone', attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'Lightning']}, origin='Mỹ', weight=300),
    m(123, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Apple iPhone 13 Pro Xanh Sierra', 18_990_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'Lightning']}, origin='Mỹ', weight=400),
    m(124, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Apple iPhone X Đen', 5_990_000, 'phone', attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'Lightning']}, origin='Mỹ', weight=350),
    m(125, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Oppo A57 Vàng Hồng', 2_990_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Trung Quốc', weight=350),
    m(126, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Oppo F19 Pro Plus 5G', 7_490_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=380),
    m(127, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Oppo K1 Màn Hình Giọt Nước', 3_490_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Trung Quốc', weight=360),
    m(128, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Realme C35 Pin 5000mAh', 3_690_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=380),
    m(129, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Realme X Camera Thò Thụt', 4_290_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=380),
    m(130, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Realme XT Camera 64MP', 4_590_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=380),
    m(131, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Samsung Galaxy S7 Đen Bóng', 2_490_000, 'phone', attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Hàn Quốc', weight=350),
    m(132, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Samsung Galaxy S8 Màn Hình Vô Cực', 3_290_000, 'phone', attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Hàn Quốc', weight=350),
    m(133, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Samsung Galaxy S10 Prism Black', 5_490_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Hàn Quốc', weight=360),
    m(134, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Vivo S1 Xanh Ngọc', 3_990_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Trung Quốc', weight=370),
    m(135, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Vivo V9 Vàng Đồng', 2_790_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Trung Quốc', weight=360),
    m(136, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Vivo X21 Đỏ Cảm Biến Vân Tay Dưới Màn Hình', 4_190_000, 'phone', attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Trung Quốc', weight=370),
    m(102, f'{P}/Phụ Kiện Điện Thoại/Cáp & Sạc', 'Đế Sạc Không Dây Apple AirPower', 1_290_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Lightning']}, origin='Mỹ', weight=300),
    m(104, f'{P}/Phụ Kiện Điện Thoại/Cáp & Sạc', 'Bộ Sạc iPhone 20W Kèm Cáp USB-C To Lightning', 449_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['USB-C', 'Lightning']}, origin='Mỹ', weight=200),
    m(105, f'{P}/Phụ Kiện Điện Thoại/Sạc Dự Phòng', 'Pin Sạc Dự Phòng Apple MagSafe Battery Pack', 2_390_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Lightning']}, origin='Mỹ', weight=200),
    m(108, f'{P}/Phụ Kiện Điện Thoại/Ốp Lưng', 'Ốp Lưng Silicon iPhone 12 Có MagSafe', 290_000, 'case_color', attrs={'Bảo hành': ['Không bảo hành']}, origin='Mỹ', weight=80),
    m(109, f'{P}/Phụ Kiện Điện Thoại/Gậy Chụp Ảnh & Livestream', 'Gậy Chụp Ảnh Tripod 3 Chân Kèm Remote Bluetooth', 199_000, attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['Bluetooth']}, origin='Trung Quốc', weight=400),
    m(110, f'{P}/Phụ Kiện Điện Thoại/Gậy Chụp Ảnh & Livestream', 'Đèn Livestream Vòng 26cm Kèm Chân Đứng', 289_000, attrs={'Bảo hành': ['6 tháng'], 'Kết nối': ['USB-C']}, origin='Trung Quốc', weight=1_200),
    m(111, f'{P}/Phụ Kiện Điện Thoại/Gậy Chụp Ảnh & Livestream', 'Gậy Selfie Cầm Tay Rút Gọn', 129_000, attrs={'Bảo hành': ['Không bảo hành'], 'Kết nối': ['Bluetooth']}, origin='Trung Quốc', weight=250),
    m(99, f'{E}/Thiết Bị Âm Thanh/Loa Thông Minh', 'Loa Thông Minh Amazon Echo Plus Thế Hệ 2', 2_490_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Mỹ', weight=1_000),
    m(103, f'{E}/Thiết Bị Âm Thanh/Loa Thông Minh', 'Loa Thông Minh Apple HomePod Mini', 2_690_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Mỹ', weight=400),
    m(100, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Bluetooth', 'Tai Nghe Bluetooth Apple AirPods Thế Hệ 3', 3_990_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'Lightning']}, origin='Mỹ', weight=150),
    m(101, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Bluetooth', 'Tai Nghe Chụp Tai Apple AirPods Max Bạc', 12_990_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'Lightning']}, origin='Mỹ', weight=500),
    m(107, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Bluetooth', 'Tai Nghe Không Dây Beats Flex Đeo Cổ', 1_490_000, attrs={'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'USB-C']}, origin='Mỹ', weight=100),

    # ---- Đồng hồ ----
    m(93, f'{H}/Đồng Hồ Nam/Đồng Hồ Dây Da', 'Đồng Hồ Nam Dây Da Nâu Lịch Tuần Trăng', 2_450_000, attrs={'Chất liệu': ['Da']}, origin='Khác', weight=200),
    m(94, f'{H}/Đồng Hồ Nam/Đồng Hồ Kim Loại', 'Đồng Hồ Longines Master Collection Automatic', 45_900_000, attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=250),
    m(95, f'{H}/Đồng Hồ Nam/Đồng Hồ Dây Da', 'Đồng Hồ Rolex Cellini Date Mặt Đen Dây Da', 289_000_000, attrs={'Chất liệu': ['Da']}, origin='Khác', weight=200),
    m(96, f'{H}/Đồng Hồ Nam/Đồng Hồ Dây Da', 'Đồng Hồ Rolex Cellini Moonphase Vàng Hồng', 659_000_000, attrs={'Chất liệu': ['Da']}, origin='Khác', weight=200),
    m(97, f'{H}/Đồng Hồ Nam/Đồng Hồ Kim Loại', 'Đồng Hồ Rolex Datejust Demi Vàng 41mm', 385_000_000, attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=250),
    m(98, f'{H}/Đồng Hồ Nam/Đồng Hồ Kim Loại', 'Đồng Hồ Rolex Submariner Mặt Đen', 315_000_000, attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=250),
    m(190, f'{H}/Đồng Hồ Nam/Đồng Hồ Kim Loại', 'Đồng Hồ IWC Ingenieur Automatic Thép', 189_000_000, attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=250),
    m(191, f'{H}/Đồng Hồ Nữ/Đồng Hồ Nữ Dây Da', 'Đồng Hồ Nữ Rolex Cellini Moonphase Dây Da', 645_000_000, attrs={'Chất liệu': ['Da']}, origin='Khác', weight=150),
    m(192, f'{H}/Đồng Hồ Nữ/Đồng Hồ Nữ Kim Loại', 'Đồng Hồ Nữ Rolex Datejust 31mm Demi', 329_000_000, attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=150),
    m(193, f'{H}/Đồng Hồ Nữ/Đồng Hồ Nữ Kim Loại', 'Đồng Hồ Nữ Dây Kim Loại Mạ Vàng Mặt Tròn', 1_290_000, attrs={'Chất liệu': ['Khác']}, origin='Nhật Bản', weight=150),
    m(194, f'{H}/Đồng Hồ Nữ/Đồng Hồ Nữ Kim Loại', 'Đồng Hồ Nữ Dây Thép Không Gỉ Mặt Đen Tối Giản', 990_000, attrs={'Chất liệu': ['Khác']}, origin='Nhật Bản', weight=150),
    m(106, f'{H}/Đồng Hồ Thông Minh/Smartwatch', 'Đồng Hồ Thông Minh Apple Watch Series 4 Vàng', 6_490_000, attrs={'Chất liệu': ['Khác']}, origin='Mỹ', weight=200),

    # ---- Thời trang nam ----
    m(83, f'{F}/Áo/Áo Sơ Mi', 'Áo Sơ Mi Nam Kẻ Caro Xanh Đen Dài Tay', 289_000, 'shirt', attrs={'Chất liệu': ['Cotton'], 'Phong cách': ['Công sở', 'Dạo phố']}, weight=300),
    m(84, f'{F}/Áo/Áo Thun', 'Áo Thun Nam Gaming In Hình Gigabyte Aorus', 199_000, 'shirt', attrs={'Chất liệu': ['Cotton'], 'Phong cách': ['Basic', 'Dạo phố']}, weight=250),
    m(85, f'{F}/Áo/Áo Sơ Mi', 'Áo Sơ Mi Flannel Nam Caro Đỏ Đen', 319_000, 'shirt', attrs={'Chất liệu': ['Cotton'], 'Phong cách': ['Dạo phố']}, weight=320),
    m(86, f'{F}/Áo/Áo Sơ Mi', 'Áo Sơ Mi Nam Ngắn Tay Hoạ Tiết Hawaii', 229_000, 'shirt', attrs={'Chất liệu': ['Lụa'], 'Phong cách': ['Dạo phố']}, weight=250),
    m(87, f'{F}/Áo/Áo Sơ Mi', 'Áo Sơ Mi Nam Kẻ Caro Xanh Rêu Form Regular', 279_000, 'shirt', attrs={'Chất liệu': ['Cotton'], 'Phong cách': ['Công sở']}, weight=300),
    m(154, f'{F}/Phụ Kiện Nam/Kính Mắt', 'Kính Mát Gọng Đồi Mồi Tròng Xanh Rêu Chống UV400', 259_000, attrs={'Chất liệu': ['Khác']}, origin='Trung Quốc', weight=100),
    m(155, f'{F}/Phụ Kiện Nam/Kính Mắt', 'Kính Mát Phi Công Gọng Kim Loại Tròng Gradient', 289_000, attrs={'Chất liệu': ['Khác']}, origin='Trung Quốc', weight=100),
    m(156, f'{F}/Phụ Kiện Nam/Kính Mắt', 'Kính Mát Phi Công Tròng Xanh Lá', 299_000, attrs={'Chất liệu': ['Khác']}, origin='Trung Quốc', weight=100),
    m(157, f'{F}/Phụ Kiện Nam/Kính Mắt', 'Kính Pixel Vui Nhộn Đi Tiệc', 99_000, attrs={'Chất liệu': ['Khác']}, origin='Trung Quốc', weight=80),
    m(158, f'{F}/Phụ Kiện Nam/Kính Mắt', 'Kính Mát Mắt Vuông Gọng Trong Suốt', 249_000, attrs={'Chất liệu': ['Khác']}, origin='Trung Quốc', weight=100),

    # ---- Thời trang nữ ----
    m(162, f'{W}/Váy Đầm/Đầm Suông', 'Đầm Xoè Hoa Nhí Xanh Cổ Tròn', 329_000, 'dress', attrs={'Chất liệu': ['Cotton'], 'Phong cách': ['Dạo phố']}, weight=350),
    m(163, f'{W}/Váy Đầm/Đầm Suông', 'Đầm Maxi Hai Dây Hoạ Tiết Đi Biển', 359_000, 'dress', attrs={'Chất liệu': ['Lụa'], 'Phong cách': ['Dạo phố']}, weight=350),
    m(164, f'{W}/Váy Đầm/Đầm Suông', 'Đầm Suông Xám Cổ Yếm Cài Nút', 389_000, 'dress', attrs={'Chất liệu': ['Polyester'], 'Phong cách': ['Công sở']}, weight=350),
    m(165, f'{W}/Váy Đầm/Đầm Suông', 'Đầm Xoè Xanh Ngọc Tay Bồng', 349_000, 'dress', attrs={'Chất liệu': ['Polyester'], 'Phong cách': ['Dự tiệc']}, weight=350),
    m(166, f'{W}/Váy Đầm/Đầm Dự Tiệc', 'Đầm Kẻ Tartan Phối Nơ Đen', 419_000, 'dress', attrs={'Chất liệu': ['Polyester'], 'Phong cách': ['Dự tiệc']}, weight=380),
    m(177, f'{W}/Váy Đầm/Đầm Dự Tiệc', 'Đầm Dạ Hội Đen Dáng Dài Cúp Ngực', 790_000, 'dress', attrs={'Chất liệu': ['Lụa'], 'Phong cách': ['Dự tiệc']}, weight=450),
    m(178, f'{W}/Váy Đầm/Chân Váy', 'Set Áo Corset Da Đỏ Kèm Chân Váy Dài', 689_000, 'dress', attrs={'Chất liệu': ['Da PU'], 'Phong cách': ['Dự tiệc']}, weight=500),
    m(179, f'{W}/Váy Đầm/Chân Váy', 'Set Áo Corset Kèm Chân Váy Đen Dáng Dài', 659_000, 'dress', attrs={'Chất liệu': ['Polyester'], 'Phong cách': ['Dự tiệc']}, weight=500),
    m(180, f'{W}/Váy Đầm/Đầm Dự Tiệc', 'Đầm Xoè Chấm Bi Trắng Đen Cổ Điển', 459_000, 'dress', attrs={'Chất liệu': ['Polyester'], 'Phong cách': ['Dự tiệc']}, weight=380),
    m(181, f'{W}/Váy Đầm/Đầm Dự Tiệc', 'Đầm Suông Đỏ Đô Tay Dài Phối Đen', 529_000, 'dress', attrs={'Chất liệu': ['Polyester'], 'Phong cách': ['Công sở', 'Dự tiệc']}, weight=380),
    m(182, f'{W}/Phụ Kiện Nữ/Bông Tai', 'Bông Tai Đá Pha Lê Xanh Lục Dáng Giọt Nước', 189_000, attrs={'Chất liệu': ['Khác']}, origin='Hàn Quốc', weight=50),
    m(183, f'{W}/Phụ Kiện Nữ/Bông Tai', 'Bông Tai Tròn Hoạ Tiết Caro Xanh', 129_000, attrs={'Chất liệu': ['Khác']}, origin='Hàn Quốc', weight=50),
    m(184, f'{W}/Phụ Kiện Nữ/Bông Tai', 'Bông Tai Hình Lá Nhiệt Đới Mạ Vàng', 149_000, attrs={'Chất liệu': ['Khác']}, origin='Hàn Quốc', weight=50),

    # ---- Giày dép ----
    m(88, f'{GN}/Giày Thể Thao Nam/Sneaker', 'Giày Nike Air Jordan 1 Đỏ Đen Cổ Cao', 3_890_000, 'men_shoes', attrs={'Chất liệu': ['Da']}, origin='Mỹ', weight=1_200),
    m(90, f'{GN}/Giày Thể Thao Nam/Giày Chạy Bộ', 'Giày Puma Future Rider Phối Màu Xanh Vàng', 1_890_000, 'men_shoes', attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=900),
    m(91, f'{GN}/Giày Thể Thao Nam/Sneaker', 'Giày Sneaker Trắng Phối Đỏ Đế Bằng', 590_000, 'men_shoes', attrs={'Chất liệu': ['Da PU']}, origin='Việt Nam', weight=900),
    m(92, f'{GN}/Giày Thể Thao Nam/Sneaker', 'Giày Sneaker Trắng Gót Đỏ Basic', 520_000, 'men_shoes', attrs={'Chất liệu': ['Da PU']}, origin='Việt Nam', weight=900),
    m(186, f'{GW}/Giày Nữ/Giày Cao Gót', 'Giày Cao Gót Calvin Klein Mũi Nhọn Da Đen', 2_190_000, 'women_shoes', attrs={'Chất liệu': ['Da']}, origin='Mỹ', weight=700),
    m(187, f'{GW}/Giày Nữ/Giày Cao Gót', 'Giày Cao Gót Ánh Kim Vàng Gót Nhọn 9cm', 459_000, 'women_shoes', attrs={'Chất liệu': ['Da PU']}, weight=650),
    m(188, f'{GW}/Giày Nữ/Giày Cao Gót', 'Giày Cao Gót Đen Mũi Nhọn Công Sở 7cm', 389_000, 'women_shoes', attrs={'Chất liệu': ['Da PU']}, weight=650),
    m(189, f'{GW}/Giày Nữ/Giày Cao Gót', 'Giày Cao Gót Đỏ Quai Mảnh Dự Tiệc', 429_000, 'women_shoes', attrs={'Chất liệu': ['Da PU']}, weight=650),
    m(185, f'{GW}/Giày Nữ/Bốt Nữ', 'Bốt Nữ Cổ Ngắn Da Phối Đen Nâu', 690_000, 'women_shoes', attrs={'Chất liệu': ['Da']}, weight=900),

    # ---- Túi ví ----
    m(172, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Xách Tay Nữ Da Xanh Navy Dáng Hộp', 659_000, attrs={'Chất liệu': ['Da PU'], 'Phong cách': ['Công sở']}, weight=700),
    m(173, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Xách Nữ Heshe Da Bò Nâu Bò', 1_890_000, attrs={'Chất liệu': ['Da'], 'Phong cách': ['Công sở', 'Dạo phố']}, origin='Khác', weight=800),
    m(174, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Xách Prada Galleria Da Saffiano Xanh Ngọc', 52_900_000, attrs={'Chất liệu': ['Da'], 'Phong cách': ['Dự tiệc']}, origin='Khác', weight=900),
    m(176, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Xách Nữ Đen Có Quai Đeo Chéo', 589_000, attrs={'Chất liệu': ['Da PU'], 'Phong cách': ['Công sở']}, weight=700),
    m(175, f'{B}/Balo Nữ/Balo Thời Trang', 'Balo Nữ Da Thuỷ Trắng Nắp Gập', 459_000, attrs={'Chất liệu': ['Da PU'], 'Phong cách': ['Dạo phố']}, weight=600),

    # ---- Thể thao ----
    m(137, f'{T}/Dụng Cụ Thể Thao/Bóng Bầu Dục', 'Bóng Bầu Dục Da Tổng Hợp Size Tiêu Chuẩn', 349_000, attrs={'Môn thể thao': ['Khác']}, origin='Mỹ', weight=500),
    m(138, f'{T}/Dụng Cụ Thể Thao/Bóng Chày', 'Bóng Chày Da Khâu Tay Thi Đấu', 129_000, attrs={'Môn thể thao': ['Khác']}, origin='Mỹ', weight=200),
    m(139, f'{T}/Dụng Cụ Thể Thao/Bóng Chày', 'Găng Tay Bắt Bóng Chày Da Thật', 690_000, attrs={'Môn thể thao': ['Khác']}, origin='Mỹ', weight=600),
    m(150, f'{T}/Dụng Cụ Thể Thao/Bóng Chày', 'Gậy Bóng Chày Hợp Kim Nhôm 32 inch', 459_000, attrs={'Môn thể thao': ['Khác']}, origin='Trung Quốc', weight=900),
    m(89, f'{T}/Dụng Cụ Thể Thao/Bóng Chày', 'Giày Đinh Bóng Chày Nike Trắng Xanh', 1_790_000, 'men_shoes', attrs={'Môn thể thao': ['Khác']}, origin='Mỹ', weight=1_000),
    m(140, f'{T}/Dụng Cụ Thể Thao/Bóng Rổ', 'Bóng Rổ Da PU Số 7 Trong Nhà Ngoài Trời', 389_000, attrs={'Môn thể thao': ['Khác']}, origin='Trung Quốc', weight=700),
    m(141, f'{T}/Dụng Cụ Thể Thao/Bóng Rổ', 'Vành Rổ Kèm Lưới Thép Treo Tường', 590_000, attrs={'Môn thể thao': ['Khác']}, origin='Trung Quốc', weight=4_000),
    m(142, f'{T}/Dụng Cụ Thể Thao/Cricket', 'Bóng Cricket Da Đỏ Khâu Tay', 189_000, attrs={'Môn thể thao': ['Khác']}, origin='Khác', weight=200),
    m(143, f'{T}/Dụng Cụ Thể Thao/Cricket', 'Gậy Cricket Gỗ Liễu Anh Quốc', 1_290_000, attrs={'Môn thể thao': ['Khác']}, origin='Khác', weight=1_300),
    m(144, f'{T}/Dụng Cụ Thể Thao/Cricket', 'Mũ Bảo Hiểm Cricket Có Lưới Chắn Mặt', 990_000, attrs={'Môn thể thao': ['Khác']}, origin='Khác', weight=900),
    m(145, f'{T}/Dụng Cụ Thể Thao/Cricket', 'Bộ Cọc Wicket Cricket Gỗ 3 Cọc', 459_000, attrs={'Môn thể thao': ['Khác']}, origin='Khác', weight=1_500),
    m(146, f'{T}/Dụng Cụ Thể Thao/Cầu Lông', 'Quả Cầu Lông Lông Vũ Ống 12 Quả', 239_000, attrs={'Môn thể thao': ['Cầu lông']}, weight=200),
    m(147, f'{T}/Dụng Cụ Thể Thao/Bóng Đá', 'Bóng Đá Da PU Khâu Máy Thi Đấu', 289_000, 'ball5', attrs={'Môn thể thao': ['Bóng đá']}, weight=450),
    m(148, f'{T}/Dụng Cụ Thể Thao/Golf', 'Bóng Golf Titleist Pro V1 Hộp 12 Quả', 1_590_000, attrs={'Môn thể thao': ['Khác']}, origin='Mỹ', weight=600),
    m(149, f'{T}/Dụng Cụ Thể Thao/Golf', 'Gậy Golf Sắt Số 7 Thân Thép', 2_890_000, attrs={'Môn thể thao': ['Khác']}, origin='Nhật Bản', weight=900),
    m(151, f'{T}/Dụng Cụ Thể Thao/Tennis', 'Bóng Tennis Nỉ Vàng Hộp 3 Quả', 129_000, attrs={'Môn thể thao': ['Khác']}, weight=250),
    m(152, f'{T}/Dụng Cụ Thể Thao/Tennis', 'Vợt Tennis Khung Carbon Đỏ 285g', 1_490_000, attrs={'Môn thể thao': ['Khác']}, origin='Trung Quốc', weight=500),
    m(153, f'{T}/Dụng Cụ Thể Thao/Bóng Chuyền', 'Bóng Chuyền Da PU Số 5 Thi Đấu', 329_000, 'ball5', attrs={'Môn thể thao': ['Khác']}, weight=400),

    # ---- Xe máy ----
    m(113, f'{X}/Xe Máy/Xe Mô Tô', 'Xe Mô Tô Thể Thao 150cc Bạc Đen', 72_900_000, attrs={'Chất liệu': ['Khác']}, origin='Nhật Bản', weight=140_000),
    m(114, f'{X}/Xe Máy/Xe Mô Tô', 'Xe Mô Tô Kawasaki Z800 Naked Bike', 279_000_000, attrs={'Chất liệu': ['Khác']}, origin='Nhật Bản', weight=230_000),
    m(115, f'{X}/Xe Máy/Xe Mô Tô', 'Mô Hình Xe Đua MotoGP Tỉ Lệ 1:12', 1_290_000, attrs={'Chất liệu': ['Khác']}, origin='Khác', weight=1_500),
    m(116, f'{X}/Xe Máy/Xe Tay Ga', 'Xe Tay Ga 125cc Phanh ABS Màu Trắng', 49_900_000, attrs={'Chất liệu': ['Khác']}, origin='Việt Nam', weight=120_000),
    m(117, f'{X}/Xe Máy/Xe Mô Tô', 'Xe Mô Tô Sportbike 1000cc Xanh Đen', 459_000_000, attrs={'Chất liệu': ['Khác']}, origin='Nhật Bản', weight=210_000),
]

# New leaves (path → attribute set of their top category is reused); new top-level categories with their set
NEW_TOPS = {PET: ('food', 500, 'pet')}

# ---------------------------------------------------------------------------------------------------------------------
# OPEN_MODELS (key 195 on, G4-C): models whose photos are openly licensed (Wikimedia Commons / Openverse — CC0, public
# domain, CC BY, CC BY-SA), found with find-photos.py and listed per key in photos.json; build.py credits them in
# CREDITS.md. They fill the industries the dummyjson set has no / few photos for (Mẹ & Bé, Đồ Chơi, Máy Ảnh, Sức Khỏe,
# Thú Cưng…) and add distinct items / brands to the main ones. Brands named here are added to catalog-seed.json.

CAM = 'Máy Ảnh & Quay Phim'
MB = 'Mẹ & Bé'
TOY = 'Đồ Chơi'

VARIANTS.update({
    'bottle_color': ('Màu Sắc', [('Xanh Ngọc', 0), ('Hồng', 0)]),
    'pacifier_color': ('Màu Sắc', [('Hồng', 0), ('Xanh Dương', 0)]),
    'baby_shoes': ('Size', [('16', 0), ('17', 0), ('18', 0), ('19', 0)]),
    'diaper_pack': ('Gói', [('Gói 1 Tã', 0), ('Combo 3 Tã', 150_000)]),
    'mask_color': ('Màu Sắc', [('Xanh Dương', 0), ('Xanh Lá', 0)]),
    'band_color': ('Màu Sắc', [('Xanh Ngọc', 0), ('Hồng', 0), ('Xanh Navy', 0)]),
    'helmet_size': ('Kích Cỡ', [('M', 0), ('L', 0), ('XL', 50_000)]),
})

KID_0 = {'Độ tuổi khuyến nghị': ['0-12 tháng']}
KID_1 = {'Độ tuổi khuyến nghị': ['1-3 tuổi']}
KID_3 = {'Độ tuổi khuyến nghị': ['3-6 tuổi']}
KID_6 = {'Độ tuổi khuyến nghị': ['6-12 tuổi']}

OPEN_MODELS = [
    # ---- Mẹ & Bé ----
    m(195, f'{MB}/Đồ Dùng Cho Bé/Bình Sữa', 'Bình Sữa Silicone Cho Bé Chống Sặc 150ml', 249_000, 'bottle_color', KID_0, origin='Hàn Quốc', weight=200),
    m(196, f'{MB}/Đồ Dùng Cho Bé/Xe Đẩy', 'Xe Đẩy Em Bé Gấp Gọn Có Mái Che Màu Đỏ', 2_890_000, None, KID_0, origin='Trung Quốc', weight=9_000),
    m(197, f'{MB}/Đồ Dùng Cho Bé/Núm Ti Giả', 'Ti Giả Silicone Mềm Cho Bé Sơ Sinh Kèm Nắp', 89_000, 'pacifier_color', KID_0, origin='Thái Lan', weight=50),
    m(198, f'{MB}/Tã & Bỉm/Tã Vải', 'Tã Vải Có Túi Chống Thấm Hoạ Tiết Vòng Tròn', 129_000, 'diaper_pack', KID_0, weight=150),
    m(199, f'{MB}/Đồ Dùng Cho Bé/Ghế Ăn Dặm', 'Ghế Ăn Dặm Chân Gỗ Khay Rời Cho Bé', 1_690_000, None, KID_1, origin='Trung Quốc', weight=7_000),
    m(200, f'{MB}/Đồ Dùng Cho Bé/Nôi & Cũi', 'Cũi Gỗ Thông Sơn Nâu Kèm Nệm Cho Bé', 2_450_000, None, KID_0, weight=18_000),
    m(201, f'{MB}/Thời Trang Bé/Giày Tập Đi', 'Giày Tập Đi Cổ Cao Buộc Dây Cho Bé Xanh Navy', 159_000, 'baby_shoes', KID_1, origin='Trung Quốc', weight=200),
    m(202, f'{MB}/Đồ Dùng Cho Bé/Ghế Ngồi Ô Tô', 'Ghế Ngồi Ô Tô Nâng Đệm Có Tựa Lưng Cho Bé', 2_190_000, None, KID_3, origin='Khác', weight=6_000),
    m(203, f'{MB}/Đồ Dùng Cho Bé/Xe Đẩy', 'Xe Đẩy Ba Trong Một Kèm Nôi Và Ghế Ngồi Ô Tô', 8_990_000, None, KID_0, origin='Mỹ', weight=14_000),

    # ---- Đồ Chơi ----
    m(204, f'{TOY}/Đồ Chơi Lắp Ráp/Lego & Xếp Hình', 'Bộ Khối Gỗ Xếp Hình Màu Pastel Cho Bé', 189_000, None, KID_1, weight=900),
    m(205, f'{TOY}/Đồ Chơi Vận Động/Xe Tập Đi', 'Xe Tập Đi Gỗ Kèm Bộ Khối Xếp Hình', 590_000, None, KID_1, weight=3_500),
    m(206, f'{TOY}/Đồ Chơi Lắp Ráp/Lego & Xếp Hình', 'Bộ Khối Gỗ Chữ Cái Tiếng Anh Cho Bé', 149_000, None, KID_3, weight=700),
    m(207, f'{TOY}/Đồ Chơi Lắp Ráp/Lego & Xếp Hình', 'Bộ Khối Xốp Ghép Hình Lập Phương Nhiều Màu', 129_000, None, KID_3, origin='Trung Quốc', weight=300),
    m(208, f'{TOY}/Đồ Chơi Vận Động/Xe Đạp Trẻ Em', 'Xe Đạp Trẻ Em Màu Đỏ Có Bánh Phụ', 1_590_000, None, KID_3, origin='Khác', weight=9_000),
    m(209, f'{TOY}/Đồ Chơi Vận Động/Xe Đạp Trẻ Em', 'Xe Thăng Bằng Khung Gỗ Sồi Bánh Đỏ', 1_190_000, None, KID_1, origin='Khác', weight=3_500),
    m(210, f'{TOY}/Đồ Chơi Mềm/Gấu Bông', 'Gấu Bông Teddy Mặc Áo Len Đỏ', 259_000, None, KID_1, origin='Trung Quốc', weight=500),
    m(211, f'{TOY}/Đồ Chơi Mềm/Gấu Bông', 'Gấu Bông Lông Xù Phong Cách Cổ Điển', 329_000, None, KID_3, origin='Khác', weight=400),
    m(212, f'{TOY}/Đồ Chơi Giáo Dục/Đồ Chơi Trí Tuệ', 'Khối Lập Phương Xoay 3x3 Sáu Màu Xoay Trơn', 69_000, None, KID_6, origin='Trung Quốc', weight=100),
    m(213, f'{TOY}/Đồ Chơi Giáo Dục/Đồ Chơi Trí Tuệ', 'Khối Lập Phương Xoay 2x2 Cỡ Nhỏ Cho Bé', 49_000, None, KID_6, origin='Trung Quốc', weight=80),
    m(214, f'{TOY}/Mô Hình/Xe Mô Hình', 'Xe Ô Tô Mô Hình Kim Loại Thể Thao Màu Cam', 159_000, None, KID_3, origin='Khác', weight=150),
    m(215, f'{TOY}/Mô Hình/Xe Mô Hình', 'Xe Đua Mô Hình Cổ Điển Màu Xanh', 139_000, None, KID_3, origin='Khác', weight=150),
    # ---- Máy Ảnh & Quay Phim ----
    m(216, f'{CAM}/Máy Ảnh/Máy Ảnh Mirrorless', 'Máy Ảnh Mirrorless Sony Alpha Full Frame Kèm Ống Kính', 52_990_000, None, {'Bảo hành': ['24 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Nhật Bản', weight=1_200, brand='Sony'),
    m(217, f'{CAM}/Máy Ảnh/Máy Ảnh Mirrorless', 'Máy Ảnh Mirrorless Canon EOS R10 Thân Máy', 21_490_000, None, {'Bảo hành': ['24 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Nhật Bản', weight=600, brand='Canon'),
    m(218, f'{CAM}/Máy Ảnh/Máy Ảnh DSLR', 'Máy Ảnh DSLR Nikon D5300 Kèm Ống Kính Zoom 18-140mm', 14_990_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi']}, origin='Nhật Bản', weight=1_100, brand='Nikon'),
    m(219, f'{CAM}/Máy Ảnh/Máy Ảnh DSLR', 'Máy Ảnh DSLR Pentax K-7 Kèm Ống Kính Kit', 8_990_000, None, {'Bảo hành': ['6 tháng'], 'Kết nối': ['Có dây']}, origin='Nhật Bản', weight=1_000, brand='Pentax'),
    m(220, f'{CAM}/Máy Ảnh/Máy Ảnh DSLR', 'Máy Ảnh DSLR Canon EOS 80D Kèm Ống Kính 18-135mm', 19_990_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Nhật Bản', weight=1_300, brand='Canon'),
    m(225, f'{CAM}/Máy Ảnh/Máy Ảnh Compact', 'Máy Ảnh Compact Nikon Coolpix Zoom Quang 10x Màu Đỏ', 3_490_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Có dây']}, origin='Nhật Bản', weight=250, brand='Nikon'),
    m(226, f'{CAM}/Máy Ảnh/Máy Ảnh Compact', 'Máy Ảnh Compact Panasonic Lumix Cảm Biến Lớn Ống Kính Leica', 15_990_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi']}, origin='Nhật Bản', weight=450, brand='Panasonic'),
    m(227, f'{CAM}/Phụ Kiện Máy Ảnh/Chân Máy', 'Chân Máy Ảnh Carbon Gập Gọn Du Lịch Bốn Khúc Chân', 4_290_000, None, {'Bảo hành': ['12 tháng']}, origin='Khác', weight=1_100),
    m(228, f'{CAM}/Phụ Kiện Máy Ảnh/Chân Máy', 'Chân Máy Bạch Tuộc Uốn Dẻo Cho Máy Ảnh Nhỏ', 249_000, None, {'Bảo hành': ['Không bảo hành']}, origin='Trung Quốc', weight=200),
    m(229, f'{CAM}/Phụ Kiện Máy Ảnh/Ống Kính', 'Ống Kính Nikon AF 50mm f/1.4 Chụp Chân Dung', 7_490_000, None, {'Bảo hành': ['12 tháng']}, origin='Nhật Bản', weight=250, brand='Nikon'),
    m(230, f'{CAM}/Phụ Kiện Máy Ảnh/Ống Kính', 'Ống Kính Canon RF 50mm f/1.2L Khẩu Lớn Cao Cấp', 54_990_000, None, {'Bảo hành': ['24 tháng']}, origin='Nhật Bản', weight=950, brand='Canon'),
    m(231, f'{CAM}/Phụ Kiện Máy Ảnh/Thẻ Nhớ', 'Thẻ Nhớ SDHC Transcend 8GB Cho Máy Ảnh', 119_000, None, {'Bảo hành': ['24 tháng']}, origin='Khác', weight=20, brand='Transcend'),
    m(232, f'{CAM}/Máy Quay/Máy Quay Phim', 'Máy Quay Phim Sony Handycam Ổ Cứng Zoom Quang Học', 5_990_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Có dây']}, origin='Nhật Bản', weight=400, brand='Sony'),
    m(233, f'{CAM}/Máy Quay/Máy Quay Phim', 'Máy Quay Chuyên Nghiệp Panasonic Cầm Tay Kèm Micro', 39_900_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Có dây']}, origin='Nhật Bản', weight=2_400, brand='Panasonic'),

    # ---- Thú Cưng ----
    m(221, f'{PET}/Thức Ăn Thú Cưng/Thức Ăn Cho Mèo', 'Hạt Khô Cho Mèo Trưởng Thành Vị Cá Ngừ Túi 1.5kg', 189_000, None, {'Khối lượng': ['1.5'], 'Hạn sử dụng': ['18']}, origin='Thái Lan', weight=1_600),
    m(222, f'{PET}/Thức Ăn Thú Cưng/Thức Ăn Cho Mèo', 'Bánh Thưởng Nhân Kem Cho Mèo Vị Gà Gói 60g', 39_000, None, {'Khối lượng': ['0.06'], 'Hạn sử dụng': ['12']}, origin='Thái Lan', weight=80),
    m(223, f'{PET}/Thức Ăn Thú Cưng/Thức Ăn Cho Chó', 'Hạt Khô Cho Chó Trưởng Thành Vị Bò Túi 3kg', 329_000, None, {'Khối lượng': ['3'], 'Hạn sử dụng': ['18']}, origin='Việt Nam', weight=3_100),
    m(234, f'{PET}/Thức Ăn Thú Cưng/Thức Ăn Cho Chó', 'Xương Gặm Da Bò Thắt Nút Làm Sạch Răng Cho Chó', 45_000, None, {'Khối lượng': ['0.1'], 'Hạn sử dụng': ['24']}, origin='Việt Nam', weight=120),
    m(224, f'{PET}/Phụ Kiện Thú Cưng/Bát Ăn & Bình Nước', 'Bát Ăn Chậm Chống Nuốt Nhanh Cho Chó Mèo Màu Xanh Lá', 119_000, None, {'Khối lượng': ['0.3'], 'Hạn sử dụng': ['60']}, origin='Trung Quốc', weight=350),
    m(235, f'{PET}/Phụ Kiện Thú Cưng/Vòng Cổ & Dây Dắt', 'Vòng Cổ Thắt Nơ Kẻ Caro Cho Chó Mèo', 79_000, None, {'Khối lượng': ['0.05'], 'Hạn sử dụng': ['60']}, weight=60),
    m(236, f'{PET}/Phụ Kiện Thú Cưng/Vòng Cổ & Dây Dắt', 'Vòng Cổ Đính Hoa Vải Hoạ Tiết Ong Cho Chó Mèo', 89_000, None, {'Khối lượng': ['0.05'], 'Hạn sử dụng': ['60']}, weight=60),
    m(237, f'{PET}/Phụ Kiện Thú Cưng/Giường & Nệm', 'Giường Sofa Có Bánh Xe Kèm Gối Cho Chó Mèo', 890_000, None, {'Khối lượng': ['6'], 'Hạn sử dụng': ['60']}, origin='Trung Quốc', weight=6_500),

    # ---- Thời Trang Nam ----
    m(241, f'{F}/Áo/Áo Thun', 'Áo Thun Nam Dài Tay In Hình Thuyền Buồm Trắng', 229_000, 'shirt', {'Chất liệu': ['Cotton'], 'Phong cách': ['Dạo phố']}, weight=280, brand='Indigo Saigon'),
    m(242, f'{F}/Áo/Áo Thun', 'Áo Thun Nam Xanh Rêu In Chữ Đối Xứng', 189_000, 'shirt', {'Chất liệu': ['Cotton'], 'Phong cách': ['Basic', 'Dạo phố']}, weight=250, brand='Coton Việt'),
    m(244, f'{F}/Áo/Áo Polo', 'Áo Polo Nam Đen Basic Thêu Ngực', 239_000, 'shirt', {'Chất liệu': ['Polyester'], 'Phong cách': ['Basic', 'Công sở']}, weight=280, brand='Indigo Saigon'),
    m(245, f'{F}/Áo/Áo Sơ Mi', 'Áo Sơ Mi Nam Kẻ Caro Madras Dài Tay', 349_000, 'shirt', {'Chất liệu': ['Cotton'], 'Phong cách': ['Dạo phố']}, weight=300, brand='Linen Hội An'),
    m(246, f'{F}/Quần/Quần Jean', 'Quần Jean Nam Ống Đứng Xanh Đậm Chỉ Vàng', 459_000, 'shirt', {'Chất liệu': ['Jean'], 'Phong cách': ['Basic', 'Dạo phố']}, weight=600, brand='Indigo Saigon'),
    m(247, f'{F}/Quần/Quần Kaki', 'Quần Kaki Nam Xám Nhạt Dáng Slim', 369_000, 'shirt', {'Chất liệu': ['Kaki'], 'Phong cách': ['Công sở']}, weight=450, brand='Coton Việt'),
    m(248, f'{F}/Quần/Quần Kaki', 'Quần Kaki Nam Xanh Navy Ống Đứng Công Sở', 389_000, 'shirt', {'Chất liệu': ['Kaki'], 'Phong cách': ['Công sở']}, weight=450, brand='Linen Hội An'),
    m(249, f'{F}/Quần/Quần Short', 'Quần Short Jean Nam Xanh Wash Ngang Gối', 249_000, 'shirt', {'Chất liệu': ['Jean'], 'Phong cách': ['Dạo phố']}, weight=350, brand='Indigo Saigon'),
    m(250, f'{F}/Quần/Quần Short', 'Quần Short Kaki Nam Vàng Nhạt Đi Biển', 219_000, 'shirt', {'Chất liệu': ['Kaki'], 'Phong cách': ['Dạo phố']}, weight=300, brand='Linen Hội An'),

    # ---- Thời Trang Nữ ----
    m(270, f'{W}/Áo Nữ/Áo Thun Nữ', 'Áo Thun Nữ Cổ Tròn Hồng Pastel Basic', 159_000, 'dress', {'Chất liệu': ['Cotton'], 'Phong cách': ['Basic']}, weight=200, brand='Coton Việt'),
    m(271, f'{W}/Quần Nữ/Quần Jean Nữ', 'Quần Jean Nữ Skinny Đen Lưng Cao Nâng Mông', 389_000, 'dress', {'Chất liệu': ['Jean'], 'Phong cách': ['Dạo phố']}, weight=500, brand='Indigo Saigon'),
    m(272, f'{W}/Quần Nữ/Quần Ống Rộng', 'Quần Ống Loe Nữ Vải Ánh Kim Vàng Champagne', 459_000, 'dress', {'Chất liệu': ['Khác'], 'Phong cách': ['Dự tiệc']}, weight=450, brand='Linen Hội An'),
    # ---- Sức Khỏe ----
    m(251, f'{K}/Thực Phẩm Chức Năng/Vitamin', 'Viên Uống Selen Và Vitamin E Tăng Đề Kháng Lọ 90 Viên', 259_000, None, {'Hạn sử dụng': ['24']}, origin='Khác', weight=150),
    m(252, f'{K}/Thực Phẩm Chức Năng/Dầu Cá & Omega-3', 'Viên Dầu Cá Omega-3 1000mg Hộp 120 Viên Nang Mềm', 389_000, None, {'Hạn sử dụng': ['24']}, origin='Mỹ', weight=250),
    m(253, f'{K}/Thiết Bị Y Tế/Máy Đo Huyết Áp', 'Máy Đo Huyết Áp Bắp Tay Microlife Màn Hình Lớn', 1_090_000, None, {'Hạn sử dụng': ['60']}, origin='Khác', weight=700, brand='Microlife'),
    m(254, f'{K}/Thiết Bị Y Tế/Máy Đo Huyết Áp', 'Máy Đo Huyết Áp Cổ Tay Điện Tử Tự Động', 690_000, None, {'Hạn sử dụng': ['60']}, origin='Trung Quốc', weight=300),
    m(255, f'{K}/Thiết Bị Y Tế/Máy Đo Huyết Áp', 'Bộ Đo Huyết Áp Cơ Đồng Hồ Kim Kèm Bóp Hơi', 390_000, None, {'Hạn sử dụng': ['60']}, origin='Trung Quốc', weight=500),
    m(256, f'{K}/Thiết Bị Y Tế/Nhiệt Kế', 'Nhiệt Kế Điện Tử Đầu Mềm Đo Nhanh Chống Nước', 129_000, None, {'Hạn sử dụng': ['60']}, origin='Trung Quốc', weight=50),
    m(257, f'{K}/Thiết Bị Y Tế/Nhiệt Kế', 'Nhiệt Kế Hồng Ngoại Đo Tai Và Trán Cho Cả Gia Đình', 590_000, None, {'Hạn sử dụng': ['60']}, origin='Khác', weight=150),
    m(258, f'{K}/Thiết Bị Y Tế/Máy Đo SpO2', 'Máy Đo Nồng Độ Oxy Máu SpO2 Kẹp Đầu Ngón Tay', 349_000, None, {'Hạn sử dụng': ['60']}, origin='Trung Quốc', weight=80),
    m(259, f'{K}/Vật Tư Y Tế/Khẩu Trang', 'Khẩu Trang Y Tế 4 Lớp Kháng Khuẩn Hộp 50 Cái', 45_000, 'mask_color', {'Hạn sử dụng': ['36']}, weight=250),
    m(238, f'{F}/Áo/Áo Khoác', 'Áo Khoác Bomber Nam Đỏ Có Túi Tay', 459_000, 'shirt', {'Chất liệu': ['Polyester'], 'Phong cách': ['Dạo phố']}, weight=600, brand='Indigo Saigon'),
    m(239, f'{F}/Áo/Áo Khoác', 'Áo Khoác Phao Nam Vàng Mù Tạt Có Mũ Trùm', 689_000, 'shirt', {'Chất liệu': ['Polyester'], 'Phong cách': ['Dạo phố', 'Thể thao']}, weight=900, brand='Coton Việt'),
    m(240, f'{F}/Áo/Áo Khoác', 'Áo Khoác Nỉ Nam Xanh Navy Cổ Đứng Khoá Kéo', 399_000, 'shirt', {'Chất liệu': ['Nỉ'], 'Phong cách': ['Basic', 'Dạo phố']}, weight=650, brand='Linen Hội An'),

    # ---- Thời Trang Nữ (tiếp) ----
    m(274, f'{W}/Áo Nữ/Áo Khoác Nữ', 'Áo Khoác Kaki Nữ Màu Be Tay Ngắn Cúc Đồng', 429_000, 'dress', {'Chất liệu': ['Kaki'], 'Phong cách': ['Dạo phố', 'Công sở']}, weight=400, brand='Linen Hội An'),
    m(275, f'{W}/Áo Nữ/Áo Thun Nữ', 'Áo Thun Nữ Đen In Hoạ Tiết Vẽ Tay', 179_000, 'dress', {'Chất liệu': ['Cotton'], 'Phong cách': ['Dạo phố']}, weight=200, brand='Indigo Saigon'),
    m(276, f'{W}/Quần Nữ/Quần Jean Nữ', 'Quần Jean Nữ Ống Suông Wash Nhạt Cạp Vừa', 359_000, 'dress', {'Chất liệu': ['Jean'], 'Phong cách': ['Basic', 'Dạo phố']}, weight=500, brand='Coton Việt'),

    # ---- Giày Dép Nam ----
    m(260, f'{GN}/Giày Tây/Giày Da Nam', 'Giày Tây Nam Oxford Da Bò Màu Bò Mũi Trơn', 1_290_000, 'men_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Công sở']}, weight=1_100),
    m(261, f'{GN}/Giày Tây/Giày Da Nam', 'Giày Tây Nam Brogue Da Nâu Đục Lỗ Hoạ Tiết', 1_390_000, 'men_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Công sở', 'Dự tiệc']}, weight=1_100),
    m(262, f'{GN}/Giày Tây/Giày Da Nam', 'Giày Tây Nam Oxford Da Đen Bóng Công Sở', 1_190_000, 'men_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Công sở']}, origin='Khác', weight=1_100),
    m(263, f'{GN}/Giày Tây/Giày Lười Nam', 'Giày Lười Nam Penny Loafer Da Bò Màu Bò', 990_000, 'men_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Công sở', 'Dạo phố']}, weight=900),
    m(264, f'{GN}/Giày Tây/Giày Lười Nam', 'Giày Lười Nam Da Lộn Nâu Chuông Tua Rua', 890_000, 'men_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Dạo phố']}, origin='Khác', weight=850),
    m(266, f'{GN}/Dép Nam/Dép Xỏ Ngón Nam', 'Dép Xỏ Ngón Nam Đế Cao Su Trắng Quai Đen', 129_000, 'men_shoes', {'Chất liệu': ['Khác'], 'Phong cách': ['Basic']}, weight=300),
    m(267, f'{GN}/Giày Thể Thao Nam/Giày Chạy Bộ', 'Giày Chạy Bộ Nam Có Đèn LED Ở Gót Chạy Đêm', 790_000, 'men_shoes', {'Chất liệu': ['Polyester'], 'Phong cách': ['Thể thao']}, origin='Trung Quốc', weight=750),

    # ---- Giày Dép Nữ ----
    m(268, f'{GW}/Giày Nữ/Bốt Nữ', 'Bốt Oxford Nữ Cổ Thấp Lót Lông Thủ Công', 1_590_000, 'women_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Dạo phố']}, origin='Khác', weight=900),
    m(269, f'{GW}/Giày Nữ/Giày Loafer Nữ', 'Giày Loafer Nữ Da Bóng Nâu Đỏ Gót Vuông', 559_000, 'women_shoes', {'Chất liệu': ['Da PU'], 'Phong cách': ['Công sở']}, origin='Nhật Bản', weight=700),
    m(273, f'{GW}/Dép Nữ/Dép Xỏ Ngón Nữ', 'Dép Xỏ Ngón Nữ Đế Trắng Quai Nâu Nhạt', 119_000, 'women_shoes', {'Chất liệu': ['Khác'], 'Phong cách': ['Basic']}, weight=250),
    m(280, f'{GW}/Dép Nữ/Sandal Nữ', 'Sandal Nữ Da Bò Quai Hậu Kiểu Menorca', 459_000, 'women_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Dạo phố']}, origin='Khác', weight=500),
    m(281, f'{GW}/Dép Nữ/Sandal Nữ', 'Sandal Đế Xuồng Nữ Quai Ngang Xanh Navy', 329_000, 'women_shoes', {'Chất liệu': ['Khác'], 'Phong cách': ['Dạo phố']}, weight=500),
    m(298, f'{GW}/Giày Nữ/Giày Búp Bê', 'Giày Búp Bê Nữ Trắng Mũi Phối Màu Đế Xanh', 359_000, 'women_shoes', {'Chất liệu': ['Da PU'], 'Phong cách': ['Basic', 'Công sở']}, origin='Khác', weight=450),
    m(299, f'{GW}/Giày Nữ/Giày Búp Bê', 'Giày Búp Bê Nữ Kẻ Caro Đỏ Đính Nơ', 259_000, 'women_shoes', {'Chất liệu': ['Khác'], 'Phong cách': ['Dạo phố']}, weight=400),
    m(300, f'{GW}/Giày Nữ/Giày Búp Bê', 'Giày Bệt Nữ Da Đỏ Mũi Nhọn Đính Nơ', 489_000, 'women_shoes', {'Chất liệu': ['Da'], 'Phong cách': ['Công sở', 'Dự tiệc']}, origin='Khác', weight=450),

    # ---- Túi Ví Nữ ----
    m(277, f'{B}/Túi Xách/Túi Đeo Chéo', 'Túi Đeo Chéo Nữ Da Bê Mềm Màu Nâu Kem', 1_190_000, None, {'Chất liệu': ['Da'], 'Phong cách': ['Dạo phố', 'Công sở']}, origin='Khác', weight=500),
    m(278, f'{B}/Ví Nữ/Ví Cầm Tay', 'Ví Cầm Tay Nữ Da Đen Kèm Dây Đeo Chéo', 289_000, None, {'Chất liệu': ['Da PU'], 'Phong cách': ['Dạo phố']}, origin='Trung Quốc', weight=250),
    m(279, f'{B}/Túi Xách/Túi Đeo Chéo', 'Túi Đeo Chéo Vải Canvas In Hoạ Tiết Cành Hoa', 259_000, None, {'Chất liệu': ['Khác'], 'Phong cách': ['Dạo phố']}, origin='Khác', weight=350),
    m(287, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Dự Tiệc Nữ Da Ánh Bạc Quai Xích', 549_000, None, {'Chất liệu': ['Da PU'], 'Phong cách': ['Dự tiệc']}, origin='Khác', weight=400),
    m(288, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Rút Dây Móc Len Đen Tua Rua Thủ Công', 219_000, None, {'Chất liệu': ['Khác'], 'Phong cách': ['Dạo phố']}, weight=200),
    m(289, f'{B}/Ví Nữ/Ví Cầm Tay', 'Ví Dài Nữ Da Màu Be Nhiều Ngăn Thẻ', 329_000, None, {'Chất liệu': ['Da PU'], 'Phong cách': ['Công sở']}, origin='Trung Quốc', weight=200),
    m(290, f'{B}/Túi Xách/Túi Xách Tay', 'Túi Xách Nữ Da Vân Cá Sấu Nâu Bò Khoá Cài', 1_890_000, None, {'Chất liệu': ['Da'], 'Phong cách': ['Công sở', 'Dự tiệc']}, origin='Khác', weight=800),

    # ---- Điện Thoại (tiếp) ----
    m(282, f'{P}/Điện Thoại/Điện Thoại Phổ Thông', 'Điện Thoại Nokia 1280 Phổ Thông Pin Lâu Có Đèn Pin', 390_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Có dây']}, origin='Khác', weight=150, brand='Nokia'),
    m(283, f'{P}/Điện Thoại/Điện Thoại Phổ Thông', 'Điện Thoại Nokia 8210 4G Màn Hình Lớn Xanh Đậm', 990_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'Có dây']}, origin='Khác', weight=180, brand='Nokia'),
    m(284, f'{P}/Điện Thoại/Điện Thoại Phổ Thông', 'Điện Thoại Nokia 3310 Cổ Điển Xanh Dương', 590_000, None, {'Bảo hành': ['6 tháng'], 'Kết nối': ['Có dây']}, origin='Khác', weight=150, brand='Nokia'),
    m(285, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Xiaomi Mi Note Pro Trắng Viền Vàng', 3_290_000, 'phone', {'Bảo hành': ['6 tháng'], 'Kết nối': ['Wifi', 'Bluetooth']}, origin='Trung Quốc', weight=350, brand='Xiaomi'),
    m(286, f'{P}/Điện Thoại/Điện Thoại Thông Minh', 'Điện Thoại Xiaomi Redmi Note 15 Đen Camera Kép', 5_490_000, 'phone', {'Bảo hành': ['12 tháng'], 'Kết nối': ['Wifi', 'Bluetooth', 'USB-C']}, origin='Trung Quốc', weight=380, brand='Xiaomi'),

    # ---- Thiết Bị Điện Tử ----
    m(291, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Bluetooth', 'Tai Nghe Bluetooth Sony Đeo Cổ Extra Bass', 1_290_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'USB-C']}, origin='Nhật Bản', weight=120, brand='Sony'),
    m(292, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Có Dây', 'Tai Nghe Chụp Tai Audio-Technica Kiểm Âm Có Dây', 2_490_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Có dây']}, origin='Nhật Bản', weight=350, brand='Audio-Technica'),
    m(293, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Có Dây', 'Tai Nghe Gaming Chụp Tai Có Micro Đỏ Đen', 590_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Có dây']}, origin='Trung Quốc', weight=400),
    m(294, f'{E}/Thiết Bị Âm Thanh/Tai Nghe Bluetooth', 'Tai Nghe True Wireless Hộp Sạc Nhôm Xám', 690_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'USB-C']}, origin='Nhật Bản', weight=80),
    m(295, f'{E}/Thiết Bị Âm Thanh/Loa Bluetooth', 'Loa Bluetooth Xách Tay Brionvega Phong Cách Cổ Điển', 3_990_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth']}, origin='Khác', weight=1_500, brand='Brionvega'),
    m(296, f'{E}/Thiết Bị Âm Thanh/Loa Bluetooth', 'Loa Bluetooth Mini Polk Nhỏ Gọn Âm Trầm Mạnh', 1_490_000, None, {'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth', 'USB-C']}, origin='Mỹ', weight=500, brand='Polk'),
    m(297, f'{E}/Thiết Bị Đeo Thông Minh/Vòng Đeo Tay Thông Minh', 'Vòng Đeo Tay Thông Minh Fitbit Theo Dõi Sức Khoẻ', 1_190_000, 'band_color', {'Bảo hành': ['12 tháng'], 'Kết nối': ['Bluetooth']}, origin='Mỹ', weight=60, brand='Fitbit'),
    m(243, f'{F}/Áo/Áo Thun', 'Áo Thun Nam Xanh Nhạt Cổ Tròn In Hoạ Tiết Nhỏ', 169_000, 'shirt', {'Chất liệu': ['Cotton'], 'Phong cách': ['Basic']}, weight=230, brand='Coton Việt'),
    m(304, f'{F}/Áo/Áo Thun', 'Áo Thun Thể Thao Nam Xanh Đen Hoạ Tiết Tia Chớp', 199_000, 'shirt', {'Chất liệu': ['Polyester'], 'Phong cách': ['Thể thao']}, weight=200, brand='Indigo Saigon'),
    m(305, f'{F}/Áo/Áo Thun', 'Áo Thun Nam Trơn Xám Than Cotton Dày Dặn', 149_000, 'shirt', {'Chất liệu': ['Cotton'], 'Phong cách': ['Basic']}, weight=250, brand='Linen Hội An'),
    m(306, f'{F}/Áo/Áo Thun', 'Áo Thun Nam Đen Thêu Hoạ Tiết Chữ Thập Trắng', 189_000, 'shirt', {'Chất liệu': ['Cotton'], 'Phong cách': ['Basic', 'Dạo phố']}, weight=230, brand='Coton Việt'),

    # ---- Ô Tô & Xe Máy ----
    m(302, f'{X}/Phụ Kiện Ô Tô/Camera Hành Trình', 'Camera Hành Trình Ô Tô Màn Hình Kép Quay Trước Sau', 1_290_000, None, {'Chất liệu': ['Nhựa']}, origin='Trung Quốc', weight=300),
    m(303, f'{X}/Phụ Kiện Xe Máy/Áo Mưa', 'Áo Mưa Poncho Vải Dù Rằn Ri Có Mũ Trùm', 189_000, None, {'Chất liệu': ['Nhựa']}, weight=450),
    # ---- Nhà Cửa (đồ điện gia dụng) ----
    m(307, f'{N}/Đồ Dùng Nhà Bếp/Thiết Bị Bếp Điện', 'Ấm Siêu Tốc Thuỷ Tinh Xanh Có Đèn Báo 1.7L', 359_000, None, {'Chất liệu': ['Thuỷ tinh'], 'Công suất': ['2000']}, origin='Trung Quốc', weight=1_300, brand='HomeLux'),
    m(308, f'{N}/Đồ Dùng Nhà Bếp/Thiết Bị Bếp Điện', 'Ấm Đun Nước Siêu Tốc Russell Hobbs Vạch Nước Xanh', 790_000, None, {'Chất liệu': ['Nhựa'], 'Công suất': ['2200']}, origin='Khác', weight=1_200, brand='Russell Hobbs'),
    m(309, f'{N}/Đồ Dùng Nhà Bếp/Thiết Bị Bếp Điện', 'Ấm Siêu Tốc Bosch Thân Kính Đế Xoay 1.7L', 1_190_000, None, {'Chất liệu': ['Thuỷ tinh'], 'Công suất': ['2400']}, origin='Khác', weight=1_400, brand='Bosch'),
    m(310, f'{N}/Đồ Dùng Nhà Bếp/Nồi Cơm Điện', 'Nồi Cơm Điện Nắp Rời Lòng Inox Màu Bạc', 690_000, None, {'Chất liệu': ['Inox'], 'Công suất': ['700']}, origin='Khác', weight=3_000, brand='Bếp Việt'),
    m(311, f'{N}/Đồ Dùng Nhà Bếp/Nồi Cơm Điện', 'Nồi Cơm Điện Tử Panasonic Màn Hình Cảm Ứng 1L', 1_890_000, None, {'Chất liệu': ['Nhựa'], 'Công suất': ['600']}, origin='Nhật Bản', weight=3_200, brand='Panasonic'),
    m(312, f'{N}/Đồ Dùng Nhà Bếp/Nồi Cơm Điện', 'Nồi Hấp Điện Đa Năng Tatung Xanh Cốm Cổ Điển', 1_490_000, None, {'Chất liệu': ['Inox'], 'Công suất': ['800']}, origin='Khác', weight=3_500, brand='Tatung'),
    m(313, f'{X}/Phụ Kiện Xe Máy/Mũ Bảo Hiểm', 'Mũ Bảo Hiểm Fullface Trắng Kính Chắn Gió Trong Suốt', 1_290_000, 'helmet_size', {'Chất liệu': ['Nhựa']}, origin='Khác', weight=1_500),
    m(314, f'{X}/Phụ Kiện Xe Máy/Mũ Bảo Hiểm', 'Mũ Bảo Hiểm Lật Hàm Đen Bóng Kính Khói', 890_000, 'helmet_size', {'Chất liệu': ['Nhựa']}, origin='Trung Quốc', weight=1_600),

    # ---- Sắc Đẹp ----
    m(315, f'{S}/Trang Điểm/Son Môi', 'Son Thỏi Hồng Đất Chất Kem Mềm Môi', 159_000, None, {'Loại da phù hợp': ['Mọi loại da']}, origin='Hàn Quốc', weight=40, brand='Lumina Beauty'),

    # ---- Nhà Cửa (nồi chảo) ----
    m(317, f'{N}/Đồ Dùng Nhà Bếp/Nồi & Chảo', 'Bộ Chảo Gốm Chống Dính Nhiều Màu Đáy Từ', 890_000, 'pot', {'Chất liệu': ['Gốm sứ']}, origin='Khác', weight=1_200, brand='Kuhn Rikon'),
    m(318, f'{N}/Đồ Dùng Nhà Bếp/Nồi & Chảo', 'Chảo Chống Dính Đen Cán Nhựa Cách Nhiệt', 199_000, 'pot', {'Chất liệu': ['Khác']}, weight=900, brand='Bếp Việt'),
]

# Brands of dummyjson models whose name already says the brand (the model lines above stay as they are)
BRANDS = {
    1: 'Essence', 6: 'Calvin Klein', 7: 'Chanel', 8: 'Dior', 9: 'Dolce & Gabbana', 10: 'Gucci', 18: 'Whiskas',
    34: 'Nescafé', 78: 'Apple', 79: 'Asus', 80: 'Huawei', 81: 'Lenovo', 82: 'Dell', 88: 'Nike', 89: 'Nike',
    90: 'Puma', 94: 'Longines', 95: 'Rolex', 96: 'Rolex', 97: 'Rolex', 98: 'Rolex', 99: 'Amazon', 100: 'Apple',
    101: 'Apple', 102: 'Apple', 103: 'Apple', 104: 'Apple', 105: 'Apple', 106: 'Apple', 107: 'Beats', 108: 'Apple',
    114: 'Kawasaki', 118: 'Attitude', 119: 'Olay', 120: 'Vaseline', 121: 'Apple', 122: 'Apple', 123: 'Apple',
    124: 'Apple', 125: 'Oppo', 126: 'Oppo', 127: 'Oppo', 128: 'Realme', 129: 'Realme', 130: 'Realme', 131: 'Samsung',
    132: 'Samsung', 133: 'Samsung', 134: 'Vivo', 135: 'Vivo', 136: 'Vivo', 148: 'Titleist', 159: 'Apple',
    160: 'Samsung', 161: 'Samsung', 173: 'Heshe', 174: 'Prada', 186: 'Calvin Klein', 190: 'IWC', 191: 'Rolex',
    192: 'Rolex',
    # Nhà Cửa: house brands of the sample shops (fictional), so the industry's brand filter has real choices
    11: 'Mộc An', 13: 'Mộc An', 15: 'Mộc An', 44: 'Mộc An', 47: 'Mộc An', 53: 'Mộc An', 51: 'HomeLux', 56: 'HomeLux',
    61: 'HomeLux', 66: 'HomeLux', 12: 'HomeLux', 14: 'HomeLux', 48: 'Bếp Việt', 50: 'Bếp Việt', 52: 'Bếp Việt',
    57: 'Bếp Việt', 64: 'Bếp Việt', 65: 'Bếp Việt', 68: 'Bếp Việt', 71: 'Bếp Việt',
}

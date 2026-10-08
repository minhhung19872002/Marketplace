# Sample catalogue: every model is one real studio photo set (dummyjson.com product id) placed in the leaf category it
# shows, with a natural Vietnamese name, a list price in VND, a variant scheme that fits the item and the industry
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
    m(35, f'{G}/Thực Phẩm Tươi/Rau Củ', 'Khoai Tây Đà Lạt Loại 1', 32_000, 'weight1kg', attrs={'Khối lượng': ['500'], 'Hạn sử dụng': ['20']}, weight=1_000),
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

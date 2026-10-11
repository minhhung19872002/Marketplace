# G-VIS vòng 2 (hoàn thiện) — nguyên văn yêu cầu của anh Hùng, 09/10/2026

> Lưu nguyên văn để không mất khi phiên làm việc tự tóm tắt. Tiến độ ở cuối tệp và trong `docs/plans/PROGRESS.md`.

Anh xem bản G-VIS trên site thật ở khổ 1270px: đẹp lên rõ rệt. Header, hero đồ hoạ, icon nhiều màu, danh mục ảnh thật, Flash Sale, dải 3 banner và Gợi ý hôm nay đã có cảm giác Shopee. Chờ CI của b8c5b7f xanh rồi làm vòng 2 dưới đây. Chỉ sửa giao diện và dữ liệu hiển thị, không đổi luồng nghiệp vụ.

## 1. Lỗi thấy ngay

1. **Giá gạch bằng giá bán ở trang chi tiết** (Áo Khoác Bomber Nam Đỏ: gạch "459.000₫ – 479.000₫" rồi bán "459.000₫ – 479.000₫"). → Chỉ gạch khi giá gốc > giá bán, áp cho cả khoảng giá theo phân loại. Thêm unit test, và E2E quét 50 sản phẩm ngẫu nhiên không được có trường hợp giá gạch ≤ giá bán.
2. **Logo shop Mall vẫn là chữ viết tắt** (SH, FA, SK, BC, TZ, HL, BP) và **chữ bị cắt mất phần trên, phần dưới** trong khung viên thuốc. → Mỗi shop một logo dạng chữ đầy đủ tên (vd "Fashionista", "SportKing" viết bằng font và màu riêng, có thể kèm 1 biểu tượng nhỏ), vừa khít khung, không cắt chữ. Dùng lại đúng logo này ở trang shop, khối shop trong trang chi tiết và các ô Mall.
3. **Ưu đãi ở ô Mall bị cắt** ("Quà tặng cho đơn từ…") → rút gọn cho vừa 1 dòng ("Quà tặng đơn 300K") hoặc cho xuống 2 dòng.
4. **Gợi ý hôm nay lặp sản phẩm**: trong 18 ô đầu, Dao Bào Vỏ Rau Củ xuất hiện 3 lần; Khay Làm Đá, Áo Bomber Đỏ, Áo Thun Xám, Dụng Cụ Bào 4 Mặt mỗi thứ 2 lần (cùng mẫu, khác shop). → Mỗi trang feed tối đa 1 tin cho mỗi mẫu (template), trộn đều ngành, không để 2 ô liền nhau cùng ngành. Áp dụng như vậy cho Sản phẩm đã xem, Có thể bạn cũng thích, Flash Sale.
5. **Thanh tiêu đề "GỢI Ý HÔM NAY" dính xuống tận chân trang** và che nút "Xem thêm". → Thanh này chỉ dính trong phạm vi section (sticky bên trong container của section), hết section thì trôi đi.
6. **Tìm kiếm hàng đầu** vẫn ghi "Đã bán 85" → đổi về "Bán x+ / tháng" như đã chốt (tính theo đơn 30 ngày). Ô đầu đang dùng ảnh bìa có chữ "ĐỔI TRẢ 15 NGÀY" bị dải xám che mất chữ → section này chỉ dùng ảnh thường, không dùng ảnh bìa có chữ.
7. **Hero và banner phụ thứ 2 cùng nói "Freeship ₫30K"** → banner phụ đổi sang nội dung khác (vd "Hàng mới về", "Deal 1K", "ShopHub Mall chính hãng") để 3 vị trí đầu trang không trùng ý.
8. Thanh "ĐANG BÁN CHẠY" của Flash Sale: chữ bị chia 2 màu ở ranh giới phần đã tô (nửa trắng nửa đỏ, khó đọc) → chữ luôn một màu, có viền/bóng nhẹ, hoặc đặt chữ trên lớp riêng màu trắng có `text-shadow`.

## 2. Hoàn thiện thêm
- **Trang chi tiết**: gallery tối thiểu 4 ảnh (ảnh bìa + ảnh trim + 2 ảnh crop cận chi tiết sinh tự động); nếu sản phẩm có màu thì có chip **Màu** có ảnh nhỏ bên cạnh chip Kích cỡ.
- **"Sản phẩm đã xem"** chỉ có 4 ô, để trống nửa phải → ít hơn 6 ô thì cho các ô giãn đều, hoặc ẩn section khi < 3 sản phẩm.
- **Mobile 390px**: chụp lại trang chủ, chi tiết, danh mục, giỏ và thanh toán sau vòng này, ghép cạnh nhau với bản desktop; chấm điểm như vòng 1.

## 3. Kiểm tra và báo cáo
- Chạy đủ bộ test như G-VIS; Lighthouse mobile trang chủ vẫn ≥ 85.
- Ảnh ghép trước/sau cho từng mục 1–8 ở `.ui-review/vis2/`.
- Báo cáo theo format cũ. Xong thì push luôn, không cần chờ anh bảo, và báo kết quả CI.

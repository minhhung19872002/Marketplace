# G-VIS vòng 4 — giỏ hàng, thanh toán và tên sản phẩm (12/10/2026)

Hai mục "còn tồn" của vòng 3. Ảnh trước / sau ở `.ui-review/vis4/before/` và `.ui-review/vis4/after/`:
- giỏ và thanh toán ở 375 / 768 / 1024 / 1440 px;
- trang chủ, chi tiết, danh mục ở 375 / 1440 px.

Ảnh ghép cạnh nhau ở `.ui-review/vis4/compare/`. Thư mục `.ui-review/` không đưa vào repo.

| # | Việc | Lỗi / quyết định | Commit | Phép thử |
|---|---|---|---|---|
| 1 | Ô chọn và nút chọn ở giỏ / thanh toán mang màu thương hiệu; ô chọn shop cùng cỡ, cùng cột với ô của dòng | L199 | `8e0784e` | e2e `gvis4.spec` "Mọi ô chọn…" |
| 2 | Thanh toán ở 375 px: ảnh + tên chiếm cả hàng, tên 2 dòng, giá dưới tên, "x1" bên phải | L200 | `8e0784e` | e2e `gvis4.spec` "Thanh toán ở 375 px…" |
| 3 | Dòng voucher: tiêu đề và "n mã dùng được ›" chung một hàng | L201 | `8e0784e` | e2e `gvis4.spec` "Dòng voucher…" |
| 4 | Vận chuyển: một dòng đơn vị đã chọn, "Thay đổi" mở danh sách; lời nhắn bên trái, vận chuyển bên phải; hàng tên cột mỗi shop | #216 | `8e0784e` | e2e `gvis4.spec` "Vận chuyển…" |
| 5 | Header gọn cho trang thanh toán (logo, "Thanh toán", trợ giúp) | #217 | `8e0784e` | e2e `gvis4.spec` "Trang thanh toán dùng header gọn…" |
| 6 | Tên sản phẩm, cụm shop thêm vào, tầng / lựa chọn phân loại, dòng đầu mô tả viết hoa đầu câu; giữ thương hiệu, dòng máy, địa danh | L202 | (commit kế) | `SeedProductNameTests`, `SentenceCaseProductNamesMigrationTests`; migration `SentenceCaseProductNames` |

## Bảng chấm điểm so với Shopee

Điểm 1–10 do người làm tự chấm bằng mắt, không phải số đo máy. Không có ảnh chụp Shopee cho giỏ / thanh toán trong
`.ui-review/shopee-ref/`, nên hai trang này so với bố cục Shopee quen thuộc (giỏ: bảng cột theo shop, thanh tổng dính
đáy; thanh toán: header gọn, địa chỉ viền phong bì, mỗi shop một khối, một dòng vận chuyển "Thay đổi").

| Khu vực | Trước vòng 4 | Sau vòng 4 | Còn khác Shopee |
|---|---|---|---|
| Giỏ hàng — 1440 px | 7,5 (ô chọn shop xanh, lệch cột) | 8,5 | Shopee có dòng "Giảm ₫… phí vận chuyển" mỗi shop và Xu ngay ở thanh tổng |
| Giỏ hàng — 375 px | 7 (dòng voucher vỡ) | 8 | — |
| Thanh toán — 1440 px | 7 (header đầy đủ, 2–3 lựa chọn vận chuyển mở sẵn mỗi shop) | 8,5 | Shopee để bảng tổng ở cuối trang; ở đây giữ cột tóm tắt bên phải, dính khi cuộn |
| Thanh toán — 375 px | 6 (tên còn 2 chữ, voucher vỡ dòng) | 8 | — |
| Tên sản phẩm (mọi trang) | 6 (Viết Hoa Từng Chữ) | 8,5 | Tên gieo vẫn dài kiểu "… - Chính hãng" như tin đăng thật |

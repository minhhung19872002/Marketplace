# G-VIS vòng 6 — "0 đã bán", khoảng cách ô Mall (12/10/2026)

Góp ý sau khi xem bản `6cbe39c`. Ảnh trước / sau ở `.ui-review/vis6/before/` và `.ui-review/vis6/after/` (ô Mall ở
1120 / 1440 / 800 / 390 px; trang chủ, chi tiết, danh mục ở 390 / 800 px). Thư mục `.ui-review/` không đưa vào repo.

| # | Việc | Lỗi | Phép thử |
|---|---|---|---|
| 1 | Số đã bán = 0 thì không hiện dòng "đã bán" trên mọi thẻ (trang chủ, danh mục, tìm kiếm, shop, "Có thể bạn cũng thích") và ở trang chi tiết; giá vẫn căn trái, thẻ không đổi chiều cao | L210 | vitest `money.test.ts` (`soldLine`); e2e `gvis6.spec` "Thẻ sản phẩm…", "Trang chi tiết…" |
| 2 | Ô thương hiệu Mall cách nhau 10 px cả ngang lẫn dọc; mọi ảnh trong cùng một khung, cùng tỉ lệ và cùng chiều cao ở hai hàng (kể cả ô "Xem tất cả") | L211 | e2e `gvis6.spec` "Ô thương hiệu Mall…" ở 1440 / 1120 / 800 / 390 px |

## Bảng chấm điểm so với Shopee

| Khu vực | Trước vòng 6 | Sau vòng 6 | Còn khác Shopee |
|---|---|---|---|
| Thẻ sản phẩm | 8,5 ("0 đã bán" trên hàng mới) | 9 | — |
| Trang chi tiết — dòng sao / đánh giá / đã bán | 8,5 | 9 | — |
| Trang chủ — ô thương hiệu Mall | 9 (ô dính nhau, ô cuối khác cỡ) | 9,5 | Ảnh vuông bị cắt trên / dưới cho vừa khung chữ nhật cạnh banner |

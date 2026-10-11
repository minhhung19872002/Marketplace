# G-VIS vòng 3 — sau khi anh xem site thật ở khổ ~800 px (11–12/10/2026)

Ảnh trước / sau: `.ui-review/vis3/before/`, `.ui-review/vis3/after/` (5 trang × 390 / 768 / 800 / 1024 / 1270 / 1440 px),
ghép cạnh nhau ở `.ui-review/vis3/compare/`. Ảnh tham chiếu Shopee: `.ui-review/shopee-ref/` (thư mục không đưa vào repo).

| # | Việc | Lỗi | Commit | Phép thử |
|---|---|---|---|---|
| 1 | Thanh trên header vỡ dòng ở 769–1023 px | L193 | `e48e4d9` | e2e `gvis3.spec` "Thanh trên cùng của header…" |
| 2 | Banner Mall cắt chữ; không bao giờ cắt ảnh có chữ (đo thêm thấy dải 3 banner cũng mất ~5 %) | L194 | `d340985` | e2e `gvis3.spec` "Banner có chữ trong ảnh không bao giờ bị cắt" |
| 3 | Ảnh ô thương hiệu Mall: món bán chạy có ảnh bìa, khung vuông lấp kín | L195 | `a1e9b7b` | `StorefrontExtrasTests.A_mall_tile_shows_a_best_seller_with_an_ad_cover…` |
| 4 | Một logo shop ở mọi nơi (khối shop, trang shop, shop liên quan, theo dõi, gợi ý header, hàng shop của danh mục) | L196 | `6745479` | e2e `gvis3.spec` "Một logo shop duy nhất…" |
| 5 | Trang danh mục: "Shop chính hãng" tách khỏi "Thương hiệu" | L197 | `18acb22` | `StorefrontExtrasTests.The_category_page_lists_official_shops…` |
| 6 | Độ lấp ảnh: ảnh cận cảnh đứng đầu 1/3 tin đăng; 8 ảnh nền trơn < 85 % | L198 | `6f5e374` | `GalleryOrderTests`; migration `CloseUpsLastInGalleries` |
| 7 | Kiểm thử thị giác 6 khổ × 5 trang; e2e chặn vỡ dòng thanh trên và cuộn ngang | — | `e48e4d9` | e2e `gvis3.spec` "Không trang nào cuộn ngang…" |

## Bảng chấm điểm so với ảnh Shopee

Điểm 1–10 do người làm tự chấm bằng mắt, so cạnh ảnh tham chiếu ở 1270 / 1440 px và ở 800 px; không phải số đo máy.

| Khu vực | Trước vòng 3 | Sau vòng 3 | Còn khác Shopee |
|---|---|---|---|
| Header (thanh trên + tìm kiếm) | 6 (vỡ dòng ở 800 px) | 8,5 | Không có ngôn ngữ / kết nối mạng xã hội (ngoài phạm vi) |
| Banner chính + 2 banner phụ | 8 | 8 | Ảnh vẽ, không có người mẫu |
| Lối tắt | 8 | 8 | — |
| Flash Sale | 6,5 (ảnh cận cảnh cắt mép) | 8,5 | — |
| Dải 3 banner | 7 (mất ~5 % chữ) | 8 | — |
| Danh mục | 8,5 | 8,5 | — |
| ShopHub Mall | 5 ở 800 px (banner cắt chữ, ảnh xấu) / 7 ở 1440 px | 8 | Shopee dùng ảnh tách nền trên nền trắng; ở đây theo yêu cầu dùng ảnh bìa quảng cáo |
| Tìm kiếm hàng đầu, Gợi ý hôm nay | 8 | 8,5 | — |
| Trang chi tiết — khối shop | 5 (2 logo) | 8,5 | — |
| Trang danh mục — shop / thương hiệu | 5 (trộn lẫn) | 8 | Shopee có ô thương hiệu lớn có ảnh |
| Giỏ hàng, thanh toán | 7,5 | 7,5 | Không đổi trong vòng này |

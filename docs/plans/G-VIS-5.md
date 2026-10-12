# G-VIS vòng 5 — ảnh, lịch chiến dịch, trang chi tiết, danh mục, giỏ, thanh toán (12/10/2026)

Góp ý sau khi xem site thật (bản `6cb16dc`) ở khổ ~1120 px. Ảnh trước / sau ở `.ui-review/vis5/before/` và
`.ui-review/vis5/after/`: trang chủ, chi tiết, danh mục, giỏ, thanh toán ở 390 / 800 / 1024 / 1440 px. Ảnh ghép cạnh nhau ở
`.ui-review/vis5/compare/`. Thư mục `.ui-review/` không đưa vào repo.

| # | Việc | Lỗi / quyết định | Phép thử |
|---|---|---|---|
| 1 | Ô thương hiệu Mall: ảnh chụp thường lấp hết bề ngang ô (không còn ảnh bìa chữ nhỏ) | L204 | e2e `gvis5.spec` "Ô thương hiệu Mall…"; integration `A_mall_tile_shows_the_plain_photo…` |
| 2 | Ảnh có chữ không bị đè dải chiến dịch; mỗi lưới ≤ 1/4 ảnh bìa, không hai ảnh bìa liền nhau | L203 | e2e `gvis5.spec` "Gợi ý hôm nay…"; vitest `covers.test.ts`; integration `A_card_with_an_ad_cover…` |
| 2b | Vẽ lại 126 ảnh bìa: chữ không đè sản phẩm, ≤ 25 % ảnh; CSDL cũ được thay ảnh khi khởi động | L205 | kiểm vùng trong `art-html.mjs` |
| 3 | 10.10 tự tắt sau 12/10 (giờ Việt Nam); bộ 11.11 (hero, popup, khung trên ảnh, trang sự kiện) tự bật 08/11 – hết 12/11 | L206, #218 | integration `CampaignScheduleTests`; unit `SaleWindowTests` |
| 4 | Giá viết "đ" thường (không còn ký hiệu ₫ trông như gạch chân); Mua kèm deal sốc gọn một hàng | L207, #219 | e2e `gvis5.spec` "Trang chi tiết…"; vitest `money.test.ts` |
| 5 | Danh mục con luôn có ảnh vật thật (danh mục chưa có hàng thì ẩn); nhóm lọc Shop tách khỏi Thương hiệu; danh mục con dạng chip ngang | L208, #220 | e2e `gvis5.spec` "Trang danh mục…"; integration `Every_sub_category_chip…`, `A_mall_shops_house_brand…` |
| 6a | Giỏ: dòng "Giảm ₫… phí vận chuyển đơn tối thiểu ₫…" mỗi shop, ShopHub Xu bật/tắt ở thanh tổng | #221 | e2e `gvis5.spec` "Giỏ hàng…"; integration `CartOffersTests` |
| 6b | Thanh toán: bảng tổng tiền ở cuối trang | #222 | e2e `gvis5.spec` "Thanh toán…" |
| 6c | Tên mẫu bớt đuôi kiểu tin đăng | L209 | `ShortenSampleListingNamesMigrationTests`; migration `ShortenSampleListingNames` |
| 7 | Ảnh 390 / 800 / 1024 / 1440 px của 5 trang | — | thanh trên cùng của header không vỡ dòng ở mọi khổ (công cụ chụp đo); banner Mall hiện trọn ở 1024 / 1440 px, tự ẩn dưới 940 px bề ngang khối |

## Bảng chấm điểm so với Shopee

Điểm 1–10 do người làm tự chấm bằng mắt, không phải số đo máy. Giỏ / thanh toán so với bố cục Shopee quen thuộc (không có
ảnh chụp tham chiếu cho hai trang này trong `.ui-review/shopee-ref/`).

| Khu vực | Trước vòng 5 | Sau vòng 5 | Còn khác Shopee |
|---|---|---|---|
| Trang chủ — ô thương hiệu Mall | 7 (ảnh 110 px giữa ô, ảnh bìa chữ nhỏ) | 9 | Ảnh vuông của sản phẩm bị cắt trên / dưới cho vừa ô chữ nhật |
| Trang chủ — Gợi ý hôm nay | 7 (ảnh bìa liền nhau, dải 10.10 đè chữ ảnh bìa) | 8,5 | — |
| Ảnh bìa sản phẩm | 6 (chữ chiếm nửa ảnh) | 8,5 | Vẫn là ảnh dựng từ ảnh tách nền, chưa phải ảnh chụp quảng cáo thật |
| Lịch chiến dịch | 6 (popup 10.10 lên lịch tới 2027) | 9 | — |
| Trang chi tiết — giá, ưu đãi shop | 7,5 | 8,5 | — |
| Trang danh mục | 7 (chữ "C", BeautyPro trong Thương hiệu, hàng 4 ô trống bên phải) | 8,5 | "Chăm sóc da" chưa có hàng nên tạm ẩn |
| Giỏ hàng — 1440 px | 8,5 | 9 | Chưa có "ShopHub Voucher" của sàn ngay ở thanh tổng |
| Giỏ hàng — 390 px | 8 | 8,5 | Thanh tổng cao hơn một hàng (Xu) |
| Thanh toán — 1440 px | 8,5 | 9 | — |
| Thanh toán — 390 px | 8 | 8,5 | — |
| Tên sản phẩm | 8,5 | 9 | 30 % tin còn đuôi trên dữ liệu đang chạy (tin cùng mẫu trong cùng shop, tin đã qua sử dụng); dữ liệu gieo mới ~20 % |

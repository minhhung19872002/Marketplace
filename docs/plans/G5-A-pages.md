# G5-A — Trang công khai chưa mang giao diện G-VIS

Kiểm ngày 10/10/2026 trên https://shophub.bluestar.com.vn (bản `main` = `b8c5b7f`, G-VIS vòng 1). Chỉ kiểm trang
công khai, **không đăng nhập**. Trình duyệt Chromium (Playwright), hai khổ **375 × 812** (điện thoại, chạm) và
**1440 × 900**. Mỗi trang: tải tới khi mạng rảnh, cuộn hết trang để nạp phần lười, đo cuộn ngang, ghi lỗi console
và phản hồi HTTP ≥ 400.

> Danh sách này để **phiên trên máy Windows làm tiếp sau G-VIS vòng 2**. Phiên cloud không sửa các trang này vì vòng 2
> đang sửa `web/` (trang chủ, trang sản phẩm, thẻ sản phẩm). Trước khi làm, kiểm lại từng mục trên bản đã có vòng 2:
> vòng 2 có thể đã xử lý luôn một số mục (thẻ sản phẩm, giá gạch, logo shop…).

## 1. Kết quả đo

| Trang | Đường dẫn | Cuộn ngang 375 / 1440 | Lỗi console | Ghi chú |
|---|---|---|---|---|
| Trang chủ | `/` | không / không | 0 | Mẫu tham chiếu của G-VIS |
| Tìm kiếm | `/tim-kiem?q=ao`, `?q=dien thoai` | không / không | 0 | |
| Danh mục | `/danh-muc/sac-dep`, `/danh-muc/ao` | không / không | 0 (xem mục 3) | |
| Chi tiết sản phẩm | `/san-pham/…` | không / không | 0 | Vòng 2 đang sửa |
| Shop | `/shop/shophub-official-store`, `/shop/beauty-corner` | không / không | 0 | |
| Flash Sale | `/flash-sale` | không / không | 0 | |
| Sự kiện | `/su-kien/sieu-sale-10-10` | không / không | 0 | |
| Giỏ hàng (khách) | `/gio-hang` | không / không | 0 | |
| Đăng nhập / Đăng ký / Quên mật khẩu | `/dang-nhap`, `/dang-ky`, `/quen-mat-khau` | không / không | 0 | |
| Trợ giúp | `/tro-giup`, `/tro-giup/tro-giup-voucher` | không / không | 0 | |
| Trang pháp lý | `/trang/quy-che-hoat-dong` | không / không | 0 | |
| Tra cứu vận đơn | `/tra-cuu-van-don` | không / không | 0 | |
| Tra cứu, mã không có | `/tra-cuu-van-don/SPX000000000` | không / không | **1** | `404 /api/tracking/SPX000000000`, Chrome ghi "Failed to load resource" |
| Tải ứng dụng | `/tai-ung-dung` | không / không | 0 | |
| 404 | `/khong-co-trang-nay` | không / không | 0 | |

Không trang nào cuộn ngang ở cả hai khổ. Phần tử tràn mép đều nằm trong dải cuộn ngang có `overflow: hidden`, ví dụ
lối tắt, "Tìm kiếm hàng đầu", khung giờ Flash Sale và thanh sắp xếp giá.

## 2. Trang chưa mang giao diện G-VIS

Mọi trang đã có **header cam sáng** (`--sh-gradient-header`) và **thẻ sản phẩm G-VIS** (nhãn nằm trong dòng, giá
"79.000₫"), vì hai thứ này là thành phần dùng chung. Phần **riêng của từng trang** thì vẫn theo G2 / G3.

| Ưu tiên | Trang | Còn theo kiểu cũ | Gợi ý theo G-VIS |
|---|---|---|---|
| 1 | **Flash Sale** `/flash-sale` | Tiêu đề "Flash Sale" là chữ thường màu cam đậm, không phải wordmark. Ngay dưới lại lặp nguyên khối Flash Sale của trang chủ (wordmark + đồng hồ), nên trang có hai tiêu đề. Tab khung giờ dùng cam đậm `#c93d19` | Một dải đầu trang cam sáng có wordmark và đồng hồ. Tab khung giờ theo kiểu đặt nền cam sáng cho khung đang diễn ra. Bỏ khối lặp. Lưới 6 cột, thanh "Đã bán" như trang chủ |
| 1 | **Sự kiện** `/su-kien/:slug` | Tiêu đề trang là chữ thường. Khối "Mã giảm giá của sàn" là thẻ trắng viền mảnh kiểu G2, nút "Đăng nhập để lưu" là chữ | Thẻ voucher có răng cưa và nút "Lưu" như voucher shop trên trang sản phẩm. Tiêu đề khối dùng kiểu tiêu đề section trang chủ (in hoa qua CSS, phải thêm vào `UPPERCASE_ALLOWED` — docs/00 #209) |
| 1 | **Shop** `/shop/:slug` | Ảnh bìa mặc định là ảnh ghép sản phẩm. Khối thông tin shop, các tab và "Chương trình đang chạy" (danh sách gạch đầu dòng) theo kiểu G2. | Đầu trang shop kiểu sàn: nền ảnh bìa tối, logo tròn, nút Theo dõi / Chat, chỉ số trên một hàng. Voucher có răng cưa. "Chương trình đang chạy" thành thẻ. Logo chữ của shop (vòng 2 làm cho thẻ / trang sản phẩm, dùng lại ở đây) |
| 2 | **Tìm kiếm** `/tim-kiem` và **Danh mục** `/danh-muc/:slug` | Thanh sắp xếp và nút "Liên quan" dùng cam đậm. Thanh lọc trái theo G2. Danh mục không có **banner ngành** (đặc tả II.2). Chip danh mục con là ô viền trắng | Thanh sắp xếp nền xám nhạt, nút đang chọn cam sáng. Banner ngành ở đầu trang danh mục. Chip danh mục con có ảnh tròn như "Danh mục" trang chủ |
| 2 | **Giỏ hàng** `/gio-hang` (khi trống) | Tiêu đề "Có thể bạn cũng thích" là chữ cam có gạch dưới, khác tiêu đề section trang chủ | Dùng `Section` có tiêu đề kiểu trang chủ (xám, in hoa qua CSS). Hình minh hoạ giỏ trống kiểu sàn |
| 2 | **Đăng nhập / Đăng ký / Quên mật khẩu** | Nền hồng nhạt, cột trái liệt kê lợi ích, dưới header lớn của site | Kiểu trang đăng nhập của sàn: header gọn chỉ có logo và tên trang, nền cam sáng có tranh minh hoạ thương hiệu bên trái, thẻ đăng nhập trắng bên phải. Ở 375 px chỉ có thẻ |
| 3 | **Trợ giúp** `/tro-giup` | Băng đầu trang nền cam đậm `#c93d19` | Băng cam sáng (`--sh-gradient-header`) hoặc ảnh nền. Ô tìm kiếm có nút như header. Các chủ đề có biểu tượng màu như lối tắt trang chủ |
| 3 | **Tra cứu vận đơn** `/tra-cuu-van-don` | Thẻ nhỏ đơn độc trên nền xám | Băng đầu trang như Trợ giúp. Thẻ kết quả có dòng thời gian như chi tiết đơn |
| 3 | **404** | Chữ "404" to màu cam đậm, không có hình | Hình minh hoạ, giữ ô tìm kiếm. Thêm "Gợi ý hôm nay" bên dưới để khách không bỏ đi |
| 3 | **Tải ứng dụng** `/tai-ung-dung` | Thẻ trắng có mã QR, chữ dày | Băng cam sáng có ảnh điện thoại, hai nút cửa hàng ứng dụng (khi có) |
| 4 | **Trang pháp lý / bài trợ giúp** | Đã ổn, kiểu tài liệu | Chỉ cần breadcrumb và mục lục dính, không cần G-VIS |

Ngoài giao diện, kiểm cũng thấy mấy chỗ sau, nên làm cùng đợt:

- **Tiêu đề tab trình duyệt** vẫn là mặc định "ShopHub - Sàn thương mại điện tử" ở `/flash-sale`, `/su-kien/…`,
  `/gio-hang`, `/dang-nhap`, `/dang-ky`, `/quen-mat-khau`, `/tro-giup…`, `/trang/…`, `/tra-cuu-van-don`,
  `/tai-ung-dung`. Tìm kiếm, danh mục, sản phẩm, shop và 404 thì có tiêu đề riêng. Trang trợ giúp, pháp lý và sự kiện nằm
  trong sitemap, nên đây cũng là chuyện SEO (đặc tả 6.6).
- **Tra cứu mã không tồn tại** để lại một lỗi đỏ trong console (`404 /api/tracking/…`). Trang hiện "Không tìm thấy mã vận
  đơn" đúng, nhưng phép đo "0 lỗi console" không đạt. Có hai cách:
  - API trả 200 kèm `data: null`;
  - giữ nguyên và ghi ngoại lệ vào phép đo.
- **Tên danh mục là dữ liệu Viết Hoa Từng Chữ** ("Sắc Đẹp", "Trang Điểm", "Chăm Sóc Da"). Luật quét #209 chỉ quét mã
  nguồn, nên dữ liệu gieo lọt qua. Muốn nhất quán thì sửa trong bộ gieo, kèm migration dọn dữ liệu cũ.

## 3. Lỗi không tái hiện được — cần kiểm lại từ mạng thường

Trong hai lượt chạy liền mạch (19 trang × 2 khổ), có hai loại lỗi chỉ xuất hiện rải rác:

- Ảnh / phông trả 502: `/s3/sh-products/product/seed/p052-0_200.webp`, `p270-0_200.webp`,
  `/assets/be-vietnam-pro-latin-ext-*.woff2`.
- Hai lần trang danh mục ở 1440 px nhận tệp CSS chunk với `Content-Type: text/plain`
  (`SearchResults-CLrhtIhm.css`, rồi `ProductGrid-Djq8Udmb.css`). Trình duyệt từ chối tệp, trang trắng.

Kiểm lại:

- `curl` cùng các tệp ấy ba lần: lần nào cũng `200 text/css`.
- Mở riêng trang danh mục bốn lần liền: sạch cả bốn.

Phiên cloud đi ra Internet qua một proxy, nên nhiều khả năng lỗi nằm ở proxy chứ không phải ở site. Tuy vậy, hậu quả của
một chunk CSS hỏng là **trắng cả trang**, nên đáng kiểm lại trên máy thường hoặc trên VM:

```bash
# trên VM: có phản hồi nào khác 200 / sai content-type cho /assets/*.css không
docker compose logs nginx --since 24h | grep -E '"GET /assets/[^ ]+\.css[^"]*" (4|5)[0-9]{2}'
```

Nếu lỗi tái hiện được thì xem lại hai chỗ:

- `vite:preloadError` (bắt lỗi rồi tải lại trang một lần);
- `types` / `default_type` của nginx cho `/assets/`.

## 4. Ảnh chụp và đo lại

Ảnh chụp lúc kiểm không đưa vào repo vì sẽ cũ ngay sau vòng 2. Đo lại cuộn ngang và lỗi console bằng
`e2e/tools/site-audit.cjs`, công cụ đã có từ G4-A. Danh sách trang của nó phải gồm đủ 19 đường dẫn ở mục 1, nhất là
`/flash-sale`, `/su-kien/…`, `/tra-cuu-van-don/<mã>` và `/tai-ung-dung`. Ảnh minh hoạ của kịch bản trình diễn nằm ở
`docs/images/10-*.jpg`.

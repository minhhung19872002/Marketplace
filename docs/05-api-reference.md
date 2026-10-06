# 05 — Tham chiếu API

Gốc: `http://<máy chủ>/api` (qua gateway Nginx). Swagger đầy đủ: `http://localhost:18080/swagger` (môi trường Development).

## Quy ước chung

**Khuôn phản hồi** — mọi endpoint:

```json
{ "success": true, "data": { }, "message": "", "errors": [] }
```

**Phân trang** — `data` của danh sách phân trang:

```json
{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }
```

`page` từ 1; `pageSize` từ 1 đến 100. Thứ tự luôn kết thúc bằng khoá duy nhất (ổn định giữa các trang).

**Mã lỗi**

| HTTP | Khi nào | `message` ví dụ |
|---|---|---|
| 400 | Dữ liệu không hợp lệ; `errors` liệt kê theo trường (`field` viết camelCase) | `Dữ liệu gửi lên chưa hợp lệ.` |
| 400 | Có ký tự U+0000 trong query/thân | `Dữ liệu chứa ký tự không hợp lệ (U+0000).` |
| 401 | Thiếu / sai / hết hạn access token | `Bạn cần đăng nhập để tiếp tục.` |
| 403 | Đã đăng nhập nhưng thiếu quyền quản trị | `Bạn không có quyền thực hiện thao tác này.` |
| 404 | Không có, hoặc không thuộc về người gọi (không bao giờ 403 cho dữ liệu của người khác) | `Không tìm thấy dữ liệu yêu cầu.` |
| 409 | Xung đột nghiệp vụ / phiên bản cũ | `Tham số vừa được người khác sửa…` |
| 429 | Vượt giới hạn tốc độ (cả tầng Nginx và API) | `Bạn thao tác quá nhanh…` |
| 500 | Lỗi hệ thống — kèm mã tra cứu (trace id) | `Đã có lỗi hệ thống…` |

**Xác thực** — `Authorization: Bearer <access token>` (JWT, issuer/audience `ShopHub`, sống 15 phút). Quyền quản
trị nằm trong claim `perm` theo dạng `MODULE.ENTITY.ACTION`; `*` là quản trị cao nhất. Cấp token: Phase 2.

## Hệ thống

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/health` | công khai | Tiến trình còn sống (`Healthy`) |
| GET | `/health/ready` | công khai | Trạng thái từng phụ thuộc: postgres, redis, minio, meilisearch (503 nếu có cái lỗi) |
| GET | `/api/site/info` | công khai | Tên sàn, hotline, thư hỗ trợ, pháp nhân, địa chỉ, MST, giấy phép — lấy từ tham số hệ thống |

## Quản trị — tham số hệ thống

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/system-parameters?group=JOB` | `SYS.PARAMETER.VIEW` | Danh sách tham số (lọc theo nhóm) |
| PUT | `/api/admin/system-parameters/{key}` | `SYS.PARAMETER.UPDATE` | Sửa giá trị. Thân `{ "value": "…", "version": 123 }`; `version` cũ → 409. Giá trị sai kiểu → 400 trên trường `value`. Tham số nhóm `JOB` áp dụng lịch mới ngay |

## Quản trị — nhật ký thao tác

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/audit-logs` | `SYS.AUDIT.VIEW` | Lọc `userId`, `action` (`CREATE`/`UPDATE`/`DELETE`), `entity`, `entityId`, `from`, `to` (ISO 8601); `from > to` → 400. Mới nhất trước |
| GET | `/api/admin/jobs` | `SYS.JOB.VIEW` | Hangfire Dashboard (việc nền) |

## Xác thực (`/api/auth`, công khai, có giới hạn tốc độ)

| Phương thức | Đường dẫn | Thân | Mô tả |
|---|---|---|---|
| POST | `/otp/send` | `{ target, purpose: Register \| Login \| ResetPassword }` | Gửi mã 6 số (SMS cho SĐT, thư cho email). Chờ 60 s giữa hai lần, tối đa 5 mã/giờ/đích (409). Login/Reset trả cùng câu dù tài khoản có hay không |
| POST | `/otp/verify` | `{ target, purpose: Register \| ResetPassword, code }` | Trả `{ ticket }` dùng **một lần** cho bước kế. Sai 5 lần → mã bị khoá |
| POST | `/register` | `{ target, ticket, password, fullName, acceptTerms, device? }` | Tạo tài khoản + đăng nhập. `acceptTerms` bắt buộc (NĐ 13/2023) |
| POST | `/login` | `{ identifier, password, device? }` | `identifier` = SĐT / email / tên đăng nhập. Sai → 401 cùng một câu; sai 5 lần → khoá tạm 15 phút |
| POST | `/login-otp` | `{ phone, code, device? }` | Đăng nhập bằng mã SMS (gửi bằng `/otp/send` purpose `Login`) |
| POST | `/refresh` | `{ refreshToken? }` | Xoay refresh token (thân, hoặc cookie `sh_rt`). Token đã dùng → 401 và thu hồi cả phiên |
| POST | `/logout` | `{ refreshToken? }` | Kết thúc phiên hiện tại |
| POST | `/forgot-password` | `{ target }` | = `/otp/send` purpose `ResetPassword` |
| POST | `/reset-password` | `{ target, ticket, newPassword }` | Đặt mật khẩu mới; mọi phiên bị kết thúc |

Kết quả đăng nhập (`data`):

```json
{
  "accessToken": "eyJ…", "accessTokenExpiresAt": "2026-10-06T06:30:00Z",
  "refreshToken": "…", "refreshTokenExpiresAt": "2026-11-05T06:15:00Z",
  "user": { "id": "…", "fullName": "Nguyễn Văn An", "phone": "0900000001", "email": null, "avatarUrl": null, "mustChangePassword": false }
}
```

Trình duyệt nhận thêm cookie `sh_rt` (httpOnly, `Path=/api/auth`, `SameSite=Strict`). **Ứng dụng di động** dùng
`refreshToken` trong thân và gọi `/refresh` với `{ "refreshToken": "…" }`.

## Tài khoản của tôi (`/api/account`, cần đăng nhập — chỉ dữ liệu của chính mình)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/me` | Hồ sơ + vai trò + quyền |
| PUT | `/profile` | `{ fullName, gender: Male\|Female\|Other\|null, dateOfBirth: "YYYY-MM-DD"\|null }` |
| PUT | `/password` | `{ currentPassword, newPassword }` — các thiết bị khác bị đăng xuất |
| GET | `/sessions` | Thiết bị đăng nhập (`isCurrent` đánh dấu thiết bị này) |
| DELETE | `/sessions/{id}` | Đăng xuất từ xa (của người khác → 404) |
| POST | `/contact/otp` | `{ newValue }` — gửi mã tới SĐT/email **mới** |
| PUT | `/contact` | `{ newValue, code }` — đổi SĐT/email |
| GET | `/export` | Tải dữ liệu cá nhân |
| POST | `/delete` | `{ password }` — xoá tài khoản (ẩn danh hoá; bị chặn khi còn đơn/số dư) |
| GET/POST | `/addresses` | Danh sách / thêm địa chỉ (tối đa 10, địa chỉ đầu tiên là mặc định) |
| PUT/DELETE | `/addresses/{id}` | Sửa / xoá |
| POST | `/addresses/{id}/default` | Đặt mặc định |

Thân địa chỉ: `{ receiverName, phone, provinceCode, districtCode, wardCode, street, lat?, lng?, type: Home|Office, isDefault }`.
Mã phường phải thuộc quận, quận thuộc tỉnh (400 kèm trường lỗi).

## Danh mục hành chính (công khai)

| GET | `/api/admin-divisions` | Danh sách tỉnh/thành |
|---|---|---|
| GET | `/api/admin-divisions?parent={mã}` | Quận/huyện của tỉnh, hoặc phường/xã của quận |

## Quản trị — người dùng & phân quyền

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/users?q=&status=&adminsOnly=&page=&pageSize=` | `IAM.USER.VIEW` | Tìm người dùng |
| POST | `/api/admin/users/{id}/lock` | `IAM.USER.LOCK` | `{ reason }` — cắt mọi phiên ngay |
| POST | `/api/admin/users/{id}/unlock` | `IAM.USER.LOCK` | Mở khoá |
| PUT | `/api/admin/users/{id}/roles` | `IAM.USER.ASSIGN_ROLE` | `{ roleIds }` — luôn phải còn ≥ 1 Quản trị cao nhất |
| GET | `/api/admin/permissions` | `IAM.ROLE.VIEW` | Cây quyền |
| GET | `/api/admin/roles` | `IAM.ROLE.VIEW` | Vai trò + quyền + số người |
| POST / PUT | `/api/admin/roles[/{id}]` | `IAM.ROLE.MANAGE` | `{ code, name, description, permissions }` |
| DELETE | `/api/admin/roles/{id}` | `IAM.ROLE.MANAGE` | Không xoá được vai trò hệ thống / đang gán |

Tài khoản đang ở trạng thái "phải đổi mật khẩu" (quản trị gieo sẵn) nhận **403** "Bạn cần đổi mật khẩu trước khi tiếp
tục." ở mọi đường dẫn trừ `/api/auth/*`, `/api/account/me`, `/api/account/password`.

## Ngành hàng & tệp (Phase 3)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/categories` | công khai | Cây danh mục 3 cấp (`iconUrl` là emoji hoặc URL ảnh) |
| GET | `/api/categories/{id}/attributes` | công khai | Thuộc tính của danh mục lá |
| GET | `/api/brands?q=` | công khai | Thương hiệu |
| POST | `/api/media/{purpose}` | đăng nhập | `multipart/form-data` (`file`); `purpose`: `product`, `review`, `kyc`, `chat`, `banner`. Ảnh mã hoá lại WebP 1200/600/200, video MP4 ≤ 30 s, KYC riêng tư |
| PUT | `/api/account/avatar` | đăng nhập | `{ assetId }` — ảnh ≤ 1 MB của chính mình |

## Kênh Người Bán (`/api/seller`, đăng nhập; shop của người khác → 404, thiếu quyền nhân viên → 403)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET / POST | `/shops` | Shop của tôi / đăng ký shop mới (KYC, kho lấy hàng, tài khoản ngân hàng) |
| POST | `/shops/{shopId}/resubmit` | Gửi lại hồ sơ bị từ chối |
| PUT | `/shops/{shopId}/profile` · `/shops/{shopId}/vacation` | Hồ sơ shop · tạm nghỉ `{ until? }` |
| GET / POST | `/shops/{shopId}/products?tab=&q=&page=` | Danh sách theo tab · tạo nháp |
| GET / PUT | `/shops/{shopId}/products/{id}` | Chi tiết · sửa (trường nhạy cảm → chờ duyệt lại) |
| POST | `/shops/{shopId}/products/{id}/actions/{operation}` | `submit` / `hide` / `show` / `delete` |
| GET | `/category-suggestions?name=` | Gợi ý danh mục lá theo tên sản phẩm |
| PUT | `/shops/{shopId}/skus/{skuId}` | Sửa nhanh giá / bật-tắt |
| POST | `/shops/{shopId}/skus/{skuId}/stock-adjustments` | `{ delta, note }` — UPDATE có điều kiện, không xuống dưới số đang giữ |
| GET | `/shops/{shopId}/skus/{skuId}/movements` | Lịch sử nhập–xuất kho |

## Quản trị — ngành hàng, sản phẩm, shop

| Phương thức | Đường dẫn | Quyền |
|---|---|---|
| GET / POST | `/api/admin/categories` · POST/DELETE `/api/admin/categories/attributes[/{id}]` | `CATALOG.CATEGORY.MANAGE` |
| POST | `/api/admin/brands` | `CATALOG.BRAND.MANAGE` |
| GET | `/api/admin/products[/{id}]` · POST `…/{id}/approve` · `…/{id}/reject` `{ reason }` | `CATALOG.PRODUCT.REVIEW` |
| POST | `/api/admin/products/{id}/ban` `{ reason }` · `…/unban` | `CATALOG.PRODUCT.BAN` |
| GET | `/api/admin/shops[/{id}]` (giấy tờ KYC qua URL ký 5 phút) | `SHOP.SHOP.VIEW` |
| POST | `/api/admin/shops/{id}/approve` · `…/reject` `{ reason }` | `SHOP.SHOP.REVIEW` |
| POST | `/api/admin/shops/{id}/lock` · `…/unlock` | `SHOP.SHOP.LOCK` |
| PUT | `/api/admin/shops/{id}/labels` `{ isMall, isPreferred }` | `SHOP.SHOP.LABEL` |
| POST | `/api/admin/search/reindex` | `SYS.SEARCH.REINDEX` — dựng lại chỉ mục từ CSDL (bình thường tự đồng bộ qua outbox) |

## Trang người mua (Phase 4, công khai trừ khi ghi khác)

**Tìm kiếm** — `GET /api/search/products`

| Tham số | Ý nghĩa |
|---|---|
| `q` | Từ khoá ≤ 100 ký tự; không dấu, sai 1 ký tự (từ ≥ 4 chữ) và từ đồng nghĩa (`SEARCH.SYNONYMS`, vd. `dt` = `điện thoại`) đều khớp. Mọi từ phải xuất hiện trong tên sản phẩm, đường dẫn danh mục, thương hiệu hoặc tên shop |
| `categoryId` | Cả cây con của danh mục |
| `shopId`, `provinces` (lặp), `brands` (lặp) | Lọc |
| `minPrice`, `maxPrice` | VND; sản phẩm khớp nếu **một** SKU nằm trong khoảng; `minPrice > maxPrice` → 400 "Khoảng giá không hợp lệ…" |
| `minRating` (1–5), `mall`, `preferred`, `inStock`, `condition` (`New`/`Used`) | Lọc |
| `attrs` (lặp) | `Tên thuộc tính=Giá trị`; cùng thuộc tính = HOẶC, khác thuộc tính = VÀ |
| `sort` | `Relevance` (mặc định) · `Newest` · `BestSelling` · `PriceAsc` · `PriceDesc` — sắp xếp chọn rõ luôn thắng độ liên quan |
| `page`, `pageSize` | Mặc định 60/trang, tối đa 100 trang |

Kết quả: `{ items: ProductCard[], totalCount, page, pageSize, facets, engine }`. `facets` gồm `categories`, `provinces`,
`brands`, `ratings`, `shopTypes` (`mall`/`preferred`), `conditions`, `attributes` (theo tên thuộc tính, `value` dạng
`Tên=Giá trị`), mỗi mục `{ value, label, count }` — số đếm theo đúng bộ lọc hiện tại. `engine` = `meilisearch`, hoặc
`postgres` khi công cụ tìm kiếm ngừng (cùng kết quả & số đếm, không chịu lỗi gõ).

`ProductCard`: `{ id, name, slug, imageUrl, minPrice, maxPrice, originalPrice, discountPercent, ratingAvg, ratingCount,
soldCount, inStock, shopId, shopName, isMall, isPreferred, provinceName }`. Sản phẩm ẩn / bị khoá / shop bị khoá không bao giờ xuất hiện.

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/search/suggest?q=` | `{ keywords, products, shops }` — gợi ý khi gõ |
| GET | `/api/search/hot-keywords` | Từ khoá được tìm nhiều nhất `SEARCH.HOT_KEYWORD_DAYS` ngày (hoặc `SEARCH.HOT_KEYWORDS`) |
| GET | `/api/categories/by-slug/{slug}` | `{ category, breadcrumb, children }` cho trang danh mục |
| GET | `/api/products/{id}` | Trang sản phẩm: breadcrumb, ảnh/video, `tiers[].options[].available` (còn SKU có hàng), `skus[]` (`available` = tồn − giữ), thuộc tính, shop, `purchasable` (false khi shop tạm nghỉ / hết hàng). Ẩn/khoá → 404 |
| POST | `/api/products/{id}/views` | Đếm lượt xem — cùng người xem trong `PRODUCT.VIEW_DEDUPE_MINUTES` chỉ tính 1 (khách nhận cookie `sh_vid`) |
| GET | `/api/products/{id}/related` · `/api/products/{id}/shop-products` | Sản phẩm tương tự · cùng shop |
| GET | `/api/home/recommendations?page=&pageSize=` | Gợi ý hôm nay — ưu tiên danh mục người xem vừa xem |
| GET | `/api/home/top-categories` · `/api/home/mall` | Tìm kiếm hàng đầu · shop Mall |
| GET | `/api/viewed` | Đã xem gần đây (theo tài khoản hoặc cookie khách) |
| GET | `/api/shops/{slug}` | Trang shop (`isFollowing` khi đã đăng nhập) |

**Của chính người mua** (đăng nhập; chỉ dữ liệu của mình):

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| POST / DELETE | `/api/shops/{id}/follow` | Theo dõi / bỏ — trả số người theo dõi; theo dõi shop của chính mình → 409 |
| GET | `/api/account/followed-shops` | Shop đang theo dõi |
| POST / DELETE | `/api/account/wishlist/{productId}` | Thích / bỏ thích — trả số lượt thích (gọi lặp/song song vẫn đếm 1) |
| GET | `/api/account/wishlist?page=` · `/api/account/wishlist/ids` | Danh sách yêu thích · chỉ id (để tô tim) |

## Giỏ hàng & thanh toán (Phase 5)

**Giỏ hàng** — `/api/cart`, khách hoặc đã đăng nhập. Khách được cấp cookie httpOnly `sh_cart`; đăng nhập / đăng ký thì
giỏ khách gộp vào giỏ tài khoản (cùng SKU cộng số lượng).

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/cart` | `{ shops[{ shopId, shopName, isMall, onVacation, lines[] }], lineCount, totalQuantity, selectedQuantity, selectedSubtotal }`. Mỗi dòng: `price`, `previousPrice` (giá lúc thêm nếu đã đổi), `available`, `canBuy`, `problem` ("Hết hàng.", "Chỉ còn N sản phẩm…", "Shop đang tạm nghỉ…") — không âm thầm xoá dòng |
| POST | `/api/cart/items` | `{ skuId, quantity }` — vượt tồn → 409 |
| PUT | `/api/cart/items/{skuId}` | `{ quantity?, selected?, skuId? }` — `skuId` = đổi sang phân loại khác của cùng sản phẩm |
| DELETE | `/api/cart/items/{skuId}` · POST `/api/cart/items/remove` `{ skuIds }` | Xoá |
| PUT | `/api/cart/selection` | `{ shopId?, selected }` — chọn / bỏ chọn cả giỏ hoặc một shop |

**Thanh toán** — `/api/checkout`, cần đăng nhập. Thân chung (`CheckoutRequest`):

```json
{ "addressId": "…", "shops": [{ "shopId": "…", "carrierCode": "SIM_FAST", "voucherCode": "SHOP01GIAM10K", "note": "…" }],
  "platformVoucherCode": "SHOPHUB50", "freeshipVoucherCode": "FREESHIP", "useCoins": true, "paymentMethod": "Cod" }
```

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| POST | `/api/checkout/quote` | Báo giá các dòng **đã chọn** trong giỏ: mỗi shop có `shippingOptions` (phí, ngày nhận dự kiến), `shopVoucherOptions`; toàn đơn có `platformVouchers`, `freeshipVouchers` — **cả mã không dùng được** kèm `problem` ("Mua thêm ₫35.000 để dùng mã này.", "Mã đã hết lượt sử dụng."…); `coins { balance, max, used }`; `paymentMethods` (COD ẩn khi vượt `PAYMENT.COD_MAX_AMOUNT`); các khoản tiền; `problems`; `canPlace` |
| POST | `/api/checkout` | Header **`Idempotency-Key`** (bắt buộc) + `{ checkout: CheckoutRequest, expectedGrandTotal }`. Tạo **mỗi shop một đơn**. Cùng khoá → trả đúng checkout cũ. 409 `PRICE_CHANGED` (data = báo giá mới), 409 hết hàng / hết lượt voucher. Giới hạn 30 lần/phút/người |
| GET | `/api/checkout/{id}` | `{ status: AwaitingPayment / Placed / Expired, orders[{ code, shopName, status, grandTotal }], payment{ paymentId, status, redirectUrl, expiresAt } }` |
| POST | `/api/checkout/{id}/pay` | "Thanh toán lại" (lần thử mới) khi còn hạn |

`paymentMethod`: `Cod` (đơn vào thẳng **Chờ xác nhận**) hoặc `Simulated` (đơn **Chờ thanh toán**, hàng / voucher / xu được giữ
tới `PAYMENT.TIMEOUT_MINUTES`; quá hạn: huỷ đơn, nhả kho, trả lượt voucher còn hạn, hoàn xu).

**Thanh toán online**

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/payments/webhooks/{provider}` | công khai, **chữ ký cổng** | IPN của cổng; xử lý đúng một lần theo `event_id`; trả lời theo khuôn của cổng (`{"code":"00",…}`), sai chữ ký → 400 |
| GET | `/api/payments/simulated/{paymentId}` | công khai | Dữ liệu trang cổng giả lập (chỉ khi `SH_PAYMENT_SIMULATED=true`) |
| POST | `/api/payments/simulated/{paymentId}/success\|fail` | công khai | Nút "Thành công" / "Thất bại" của cổng giả lập — cổng ghi sổ rồi gọi webhook có chữ ký |

Webhook của SimulatedGateway: thân `{ eventId, paymentId, txnId, amount, status: "SUCCESS"|"FAILED", reason }`, header
`X-Sim-Signature` = hex HMAC-SHA256 của thân.

**Đơn mua** (`/api/orders`, của chính người mua; đơn người khác → 404)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/orders?tab=&q=&page=` | `tab`: `All`, `AwaitingPayment`, `Processing`, `Shipping`, `Completed`, `Cancelled`, `Returns`; `q` = mã đơn / tên shop / tên sản phẩm |
| GET | `/api/orders/{code}` | Chi tiết: dòng (đã chụp tên, ảnh, phân loại, giá), giảm giá phân bổ từng dòng, địa chỉ đã chụp, lịch sử trạng thái |

**Voucher & xu**

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/vouchers?shopId=` | công khai | Voucher công khai đang chạy của sàn / một shop (đăng nhập thì kèm lý do không dùng được) |
| POST | `/api/account/vouchers/{id}/claim` | đăng nhập | Lưu vào ví |
| GET | `/api/account/vouchers?tab=Valid\|ExpiringSoon\|Used\|Expired` | đăng nhập | Ví voucher |
| GET | `/api/account/coins?page=` | đăng nhập | `{ balance, expiringSoon, history }` — số dư là tổng sổ xu |
| GET / POST / PUT | `/api/seller/shops/{shopId}/vouchers[/{id}]` · POST `…/{id}/stop` | nhân viên có `MARKETING.MANAGE` | Voucher của shop (giảm tiền / %) |
| GET / POST / PUT | `/api/admin/vouchers[/{id}]` · POST `…/{id}/stop` | `PROMO.VOUCHER.MANAGE` | Voucher của sàn (giảm tiền / % / miễn ship / hoàn xu; đối tượng: mọi người / khách mới) |
| POST | `/api/admin/users/{id}/coins` | `PROMO.COIN.GRANT` | `{ delta, reason }` — cộng / thu hồi xu (không xuống dưới 0) |
| POST | `/api/admin/job-runs/{id}` | `SYS.JOB.RUN` | Chạy ngay `sys.outbox-dispatch`, `sys.counter-recompute`, `sales.payment-expiry` |

Thân voucher: `{ code, name, type: Amount|Percent|FreeShipping|CoinCashback, discountValue, discountPercentBp, maxDiscount,
minOrder, audience: Everyone|NewBuyer|ShopFollowers, categoryIds, productIds, startAt, endAt, totalQuota, perUserLimit,
isPublic, channel }` (`discountPercentBp`: phần vạn, 1200 = 12%).

## Đơn hàng & vận chuyển (Phase 6)

**Người mua** (`/api/orders/{code}`, đơn của chính mình):

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| POST | `/cancel` | `{ reason }` — huỷ trực tiếp khi **Chờ thanh toán** (huỷ cả checkout) hoặc **Chờ xác nhận**; sau khi shop xác nhận → 409 "vui lòng gửi yêu cầu huỷ" |
| POST | `/cancel-request` | `{ reason }` — khi **Chờ lấy hàng**; shop phản hồi trong `ORDER.CANCEL_REQUEST_HOURS`, quá hạn tự chấp thuận; mỗi đơn một yêu cầu đang chờ (409 nếu đã có / đã bị từ chối) |
| POST | `/received` | "Đã nhận được hàng" → **Hoàn thành** |
| POST | `/buy-again` | Đưa lại các dòng còn bán vào giỏ → `{ added, skipped }` |

`GET /api/orders/{code}` bổ sung `shipment { trackingNo, carrierName, statusLabel, events[] }`, `cancelRequest`, `autoCompleteAt` và
`actions { pay, cancel, requestCancel, confirmReceived, buyAgain }` (nút nào được bấm lúc này).

**Kênh Người Bán** (`/api/seller`, nhân viên shop; `ORDER.VIEW` để xem, `ORDER.MANAGE` để thao tác; shop khác → 404):

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/shops/{shopId}/dashboard` | Việc cần làm (chờ xác nhận, chờ lấy hàng, đang giao, yêu cầu huỷ, giao lỗi, sản phẩm bị khoá, sắp hết hàng), điểm phạt, doanh số hôm nay / 7 / 30 ngày |
| GET | `/shops/{shopId}/orders?tab=&q=&from=&to=&carrier=&paymentMethod=&page=` | `tab`: `All`, `Unpaid`, `ToConfirm`, `ToShip`, `Shipping`, `Delivered`, `Cancelled`, `CancelRequests`, `Failed`; `q` = mã đơn / mã vận đơn / tên người mua / tên sản phẩm |
| GET | `/shops/{shopId}/orders/{orderId}` | Chi tiết + người mua, ghi chú nội bộ, hạn chuẩn bị hàng |
| POST | `/shops/{shopId}/orders/prepare` | `{ orderIds[], pickupMethod: Pickup\|DropOff, pickupSlot }` → mỗi đơn `{ ok, trackingNo, error }` |
| GET | `/pickup-slots` | Khung giờ lấy hàng |
| GET | `/shops/{shopId}/orders/labels?ids=…&ids=…&size=A6\|A5` | **PDF** phiếu giao hàng (mỗi kiện một trang: mã vạch vận đơn, mã đơn, người gửi / nhận, COD, danh sách hàng) |
| GET | `/shops/{shopId}/orders/picking-list?ids=…` | **PDF** phiếu soạn hàng gộp theo SKU |
| GET | `/shops/{shopId}/orders/export?tab=&from=&to=` | **Excel** danh sách đơn (≤ 5.000 dòng) |
| POST | `/shops/{shopId}/orders/{orderId}/cancel` | `{ reason }` — shop huỷ (nhả kho, hoàn tiền nếu đã trả online) |
| POST | `/shops/{shopId}/orders/{orderId}/cancel-request` | `{ approve, rejectReason }` |
| PUT | `/shops/{shopId}/orders/{orderId}/note` | `{ note }` — ghi chú nội bộ |

**Vận chuyển & thông báo**

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/tracking/{trackingNo}` | công khai | Trạng thái + hành trình (không có thông tin cá nhân) |
| POST | `/api/logistics/webhooks/{provider}` | chữ ký hãng | Sự kiện trạng thái vận đơn; hãng giả lập: thân `{ eventId, trackingNo, status, location, description, occurredAt }`, header `X-Sim-Carrier-Signature` |
| GET | `/api/notifications?category=Order\|Promotion\|Wallet\|Activity&page=` | đăng nhập | Thông báo của mình |
| GET | `/api/notifications/unread` | đăng nhập | `{ total, byCategory }` |
| POST | `/api/notifications/{id}/read` · `/api/notifications/read-all` | đăng nhập | Đánh dấu đã đọc |

Việc nền chạy ngay được bằng `POST /api/admin/job-runs/{id}`: thêm `sales.order-automation`, `logistics.carrier-simulator`.

## Đánh giá, trả hàng & khiếu nại (Phase 7)

**Công khai**

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/products/{id}/reviews?rating=&withMedia=&withComment=&page=` | `{ summary { average, total, byStar, withMedia, withComment }, reviews }` — tên người đánh giá ẩn danh bị che |

**Người mua** (đăng nhập, chỉ đơn / yêu cầu của mình — của người khác → 404)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/orders/{code}/reviews` | Các dòng của đơn kèm đánh giá đã viết, `canReview`, `canEdit`, `deadline` |
| POST | `/api/orders/{code}/items/{orderItemId}/review` | `{ rating 1–5, content ≤ 1000, tags[], anonymous, mediaAssetIds[] ≤ 6 ảnh + 1 video }` — đơn phải **Hoàn thành**; trùng → 409 |
| PUT | `/api/reviews/{id}` | Cùng thân — sửa **một lần** |
| POST | `/api/reviews/{id}/report` | `{ reason }` — mỗi người một báo cáo |
| GET | `/api/orders/{code}/returnable` | `{ canReturn, reason, deadline, lines[{ returnable, unitRefundEstimate }] }` |
| POST | `/api/orders/{code}/returns` | `{ type: RefundOnly\|ReturnAndRefund, reason: MissingItem\|WrongItem\|Damaged\|NotAsDescribed\|Counterfeit\|Other, description ≥ 10, lines[{ orderItemId, quantity }], evidenceAssetIds[] ≥ 1 }` → yêu cầu `RT…` |
| GET | `/api/returns?page=` · `/api/returns/{code}` | Yêu cầu của mình (số tiền từng dòng, bằng chứng, lịch sử, mã vận đơn trả) |
| POST | `/api/returns/{code}/cancel` · `/accept-offer` · `/dispute` | `{ reason }` (bắt buộc khi khiếu nại) |

**Kênh Người Bán** (`/api/seller/shops/{shopId}`, quyền shop `REVIEW.MANAGE` cho đánh giá; `ORDER.VIEW` xem / `ORDER.MANAGE` xử lý trả hàng)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/reviews?rating=&replied=&page=` | Đánh giá sản phẩm của shop |
| POST | `/reviews/{id}/reply` | `{ text }` — trả lời một lần |
| GET | `/returns?status=&page=` | Yêu cầu trả hàng của shop |
| POST | `/returns/{id}/actions` | `{ action: Approve\|Reject\|OfferPartial\|ConfirmReceived, note, amount, restock, evidenceAssetIds[] }` — từ chối cần ghi chú; đề nghị một phần cần `amount` < số yêu cầu |

**Quản trị**

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/disputes?open=&page=` | `SALES.DISPUTE.RESOLVE` | Khiếu nại chờ / đã phân xử |
| POST | `/api/admin/disputes/{returnId}/decide` | `SALES.DISPUTE.RESOLVE` | `{ decision: FavorBuyer\|FavorShop, reason, refundAmount?, requireReturn }` |
| GET | `/api/admin/review-reports?status=Pending\|Upheld\|Dismissed` | `CATALOG.REVIEW.MODERATE` | Báo cáo đánh giá |
| POST | `/api/admin/review-reports/{id}/resolve` | `CATALOG.REVIEW.MODERATE` | `{ hide, reason }` — ẩn thì tính lại điểm và thu hồi xu thưởng |

`GET /api/orders/{code}` bổ sung `actions.review` và `actions.return`. Tải tệp: `POST /api/media/review` và `/api/media/evidence` (ảnh, MP4).

## Tài chính (Phase 8)

**Ví ShopHub** (`/api/wallet`, đăng nhập — chỉ ví của mình)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/wallet?page=` | `{ balance, pendingWithdrawals, hasPin, locked, bankAccounts[], history (bút toán của ví), withdrawals[] }` |
| POST | `/api/wallet/otp` | Gửi OTP tới SĐT của chính mình (đặt mật khẩu ví, thêm tài khoản ngân hàng) |
| POST | `/api/wallet/pin` | `{ otpCode, pin }` — mật khẩu ví 6 số (không chấp nhận dãy dễ đoán) |
| POST | `/api/wallet/topups` | `{ amount }` (`FINANCE.TOPUP_MIN`–`MAX`) → `{ topupId, paymentId, redirectUrl }`; tiền vào ví khi cổng gọi webhook |
| GET | `/api/wallet/topups/{id}` | Trạng thái lệnh nạp |
| POST · DELETE | `/api/wallet/bank-accounts` · `/{id}` | `{ bankCode, accountNo, accountName, otpCode }` |
| POST | `/api/wallet/withdrawals` | `{ bankAccountId, amount, pin }` |

Thanh toán bằng ví: `POST /api/checkout` thêm `walletPin` khi `paymentMethod = "Wallet"`; sai mật khẩu → 409 (còn N lần), số dư không đủ → 409 `WALLET_INSUFFICIENT`.

**Kênh Người Bán** (`/api/seller/shops/{shopId}/finance`, quyền shop `FINANCE.VIEW`; rút tiền / thêm tài khoản cần `FINANCE.WITHDRAW`)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/summary` | Chờ giải ngân, khả dụng, đang rút, tổng đã giải ngân, giới hạn rút, tài khoản ngân hàng |
| GET | `/pending?page=` | Đơn chờ giải ngân: tiền hàng, giảm giá shop, hoàn tiền shop chịu, từng loại phí, thực nhận, dự kiến giải ngân, có yêu cầu trả hàng đang mở |
| GET | `/released?from=&to=&page=` | Đơn đã giải ngân (số liệu chụp lúc giải ngân) |
| GET | `/transactions?account=ShopAvailable\|ShopPending&page=` | Bút toán của số dư |
| GET | `/report?from=&to=&format=Xlsx\|Pdf` | **Báo cáo đối soát** kỳ [from, to) |
| GET | `/fee-invoice?from=&to=` | **Hoá đơn phí sàn** (PDF) |
| GET · POST | `/withdrawals` | Lịch sử · `{ bankAccountId, amount }` |
| POST | `/otp` · `/bank-accounts` | OTP tới SĐT người thao tác · `{ bankCode, accountNo, accountName, otpCode, makeDefault }` |

**Quản trị** (`/api/admin/finance`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET · POST | `/fee-rules` | `FINANCE.FEE.MANAGE` | Biểu phí · `{ categoryId?, feeType: Fixed\|Payment\|Service, rateBp, validFrom, note }` (không lùi ngày) |
| GET | `/withdrawals?status=` | `FINANCE.WITHDRAWAL.APPROVE` | Lệnh rút (shop và ví) |
| POST | `/withdrawals/{id}/approve` · `/reject` | `FINANCE.WITHDRAWAL.APPROVE` | Duyệt & chuyển · `{ reason }` — tiền về lại số dư |
| GET | `/ledger` · `/ledger/entries?accountType=&ownerId=&refType=&refId=` | `FINANCE.LEDGER.VIEW` | Số dư tài khoản sàn, tổng số dư shop / ví, kết quả kiểm sổ · bút toán |
| GET | `/statements/{gateway\|carrier}?from=&to=` | `FINANCE.RECONCILE` | Sao kê của nhà cung cấp (CSV) |
| POST | `/reconcile/{gateway\|carrier}` | `FINANCE.RECONCILE` | multipart `from`, `to`, `file` (CSV ≤ 5 MB) → `{ statementLines, matched, issues[], statementTotal, systemTotal }` |

Việc nền chạy ngay được: `finance.settlement` (giải ngân), `finance.ledger-check` (kiểm sổ).

## Marketing (Phase 9)

**Công khai**

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/flash-sale?slotId=` | `{ serverTime, slot { startAt, endAt, running }, upcoming[], items[{ flashPrice, basePrice, quota, sold, soldPercent, perUserLimit }] }` |
| GET | `/api/products/{id}/deals` | Giá chương trình từng SKU (`price`, `basePrice`, `label`), Flash Sale đang chạy (`endAt`, `quota`, `sold`), ưu đãi combo / mua kèm / quà tặng; `serverTime` |
| GET | `/api/home/banners` | `{ main[], side[], shortcuts[], popup, popupFrequencyHours, pinnedKeywords[] }` |
| GET | `/api/campaigns/{slug}` | Trang sự kiện: các khối đã dựng sẵn (banner, voucher, Flash Sale, sản phẩm) |

Giỏ hàng trả thêm `priceLabel`; báo giá thêm `comboDiscount`, mỗi dòng `comboDiscount` / `priceLabel`, mỗi shop `gifts[]`. Đặt hàng có thể trả 409 `FLASH_SOLD_OUT` / `FLASH_USER_LIMIT`.

**Tài khoản**: `GET /api/account/membership` (hạng, chi tiêu, mốc kế tiếp) · `GET|POST /api/account/check-in` (điểm danh, 409 nếu hôm nay đã điểm danh).

**Kênh Người Bán** (`/api/seller/shops/{shopId}/marketing`, quyền shop `MARKETING.MANAGE`)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/skus?q=` | Phân loại đang bán của shop (để chọn vào chương trình) |
| GET · POST | `/promotions` | `{ type: Discount\|Combo\|AddOn\|Gift, name, startAt, endAt, productIds[], skus[{ skuId, price }], minQuantity, discountBp, discountAmount, maxAddOnQuantity, minSpend, giftSkuId, giftQuantity }`; trùng chương trình giá → 409 |
| POST | `/promotions/{id}/stop` | Dừng (giải phóng SKU khỏi chương trình giá) |
| GET · POST | `/flash-sales` | Flash Sale của shop: `{ startAt, endAt, items[{ skuId, flashPrice, quota, perUserLimit }] }` |
| GET | `/platform-slots` | Khung của sàn còn nhận đăng ký (tiêu chí) |
| POST | `/platform-slots/{slotId}/items` | Đăng ký (kiểm tiêu chí), chờ duyệt |

**Quản trị** (`/api/admin/marketing`, quyền `PROMO.MARKETING.MANAGE`)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET · POST | `/flash-slots` | Khung của sàn: `{ date, hour (∈ FLASH.SLOT_HOURS), minDiscountBp, minRating, categoryIds }` |
| POST | `/flash-items/{id}/approve` · `/reject` | Duyệt (đưa vào chương trình giá + nạp bộ đếm Redis) · `{ reason }` |
| GET · POST | `/banners` | `{ id?, position: HomeMain\|HomeSide\|Shortcut\|Category\|Popup, title, imageUrl (hoặc emoji cho lối tắt), link, startAt, endAt, sortOrder, isActive }` |
| GET · POST | `/campaigns` | `{ id?, name, slug, startAt, endAt, blocks[], isActive }` |

Việc nền chạy ngay được: `promo.flash-reconcile`, `promo.coin-expiry`.

## Chat & thông báo (Phase 10)

Realtime: hub SignalR **`/hubs/realtime`** (JWT; trình duyệt gửi `?access_token=`). Sự kiện máy chủ → client:
`chat.message` (tin nhắn), `chat.read` `{ conversationId, side, at }`, `chat.typing` `{ conversationId, side }`,
`notification` (thông báo mới). Client → máy chủ: `Typing(conversationId)`.

**Người mua** (`/api/chat`, đăng nhập; hội thoại của người khác trả 404)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| POST | `/conversations` | `{ shopId }` → mở (hoặc lấy lại) hội thoại với shop |
| GET | `/conversations?q=&page=&pageSize=` | Danh sách, tìm theo tên shop, kèm số chưa đọc |
| GET | `/unread` | Tổng tin chưa đọc |
| GET | `/conversations/{id}/messages?before=&limit=` | Tin nhắn, mới nhất trước, phân trang theo thời điểm |
| POST | `/conversations/{id}/messages` | `{ type: Text\|Image\|Product\|Order\|Voucher, text?, imageAssetId?, productId?, orderCode?, voucherId? }` — 60 tin / phút |
| POST | `/conversations/{id}/read` | Đánh dấu đã đọc (phát `chat.read`) |
| POST | `/conversations/{id}/block` · `/report` | `{ blocked }` · `{ reason }` |
| GET | `/api/shops/{shopId}/chat-stats` | Công khai: tỉ lệ phản hồi, thời gian phản hồi, hoạt động gần nhất |

Ảnh chat tải qua `POST /api/media/chat` (bucket riêng, chỉ trả URL ký hạn).

**Shop** (`/api/seller/shops/{shopId}/chat`, quyền `CHAT.MANAGE`)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/conversations?filter=All\|Unread\|Mine\|Unassigned&q=` | Hộp thư |
| GET · POST | `/conversations/{id}/messages` | Như phía người mua |
| POST | `/conversations/{id}/read` · `/assign` | Đã đọc · `{ staffUserId \| null }` |
| GET | `/staff` | Nhân viên nhận được hội thoại |
| GET · POST · DELETE | `/quick-replies` · `/quick-replies/{id}` | `{ id?, shortcut, content }` |
| GET · PUT | `/settings` | `{ autoReplyEnabled, autoReplyText, openFrom: "HH:mm:ss", openTo }` |

**Cài đặt thông báo** (`/api/notifications`, của chính người gọi)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET · PUT | `/prefs` | `{ prefs: [{ category: Order\|Promotion\|Wallet\|Activity, channel: InApp\|Email\|Sms\|Push, enabled }] }` — InApp luôn bật |
| POST | `/devices` | `{ platform, token }` — token FCM của app |

**Quản trị** (`/api/admin/marketing/broadcasts`, quyền `PROMO.MARKETING.MANAGE`): `GET` lịch sử, `POST`
`{ title, body, link?, segment: Everyone\|MemberGold\|MemberDiamond\|NoOrderYet }` → số người nhận và số bị bỏ qua
(đã nhận một thông báo khuyến mãi trong ngày).

Việc nền chạy ngay được: `engage.reminders`.

## Cổng thanh toán & hãng vận chuyển thật (Phase 11)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET · POST | `/api/payments/webhooks/vnpay` | IPN của VNPay (GET, chữ ký trên query) → `{ "RspCode": "00" \| "01" \| "02" \| "04" \| "97", "Message" }` |
| POST | `/api/payments/webhooks/momo` | IPN của MoMo (JSON ký HMAC-SHA256) → 204 khi nhận, 400 khi sai chữ ký |
| POST | `/api/logistics/webhooks/GHN?token=…` | Trạng thái vận đơn GHN (JSON `{ OrderCode, Status, Time, Reason }`) |
| POST | `/api/logistics/webhooks/GHTK?token=…` | Trạng thái vận đơn GHTK (form hoặc JSON `label_id, status_id, action_time, reason`) |
| GET | `/api/wallet/topup-gateways` | Cổng đang bật để nạp ví: `[{ method: VnPay\|MoMo\|Simulated, name }]`; `POST /api/wallet/topups` nhận thêm `method` |
| GET | `/api/seller/shops/{shopId}/orders/{orderId}/carrier-label` | Phiếu giao của hãng (PDF, GHTK); 404 khi hãng dùng phiếu ShopHub |

`paymentMethod` của checkout nhận thêm `VnPay`, `MoMo` (chỉ khi cổng bật); `payment.redirectUrl` khi đó là URL tuyệt đối
của cổng. Mã kênh vận chuyển thật: `GHN_STD`, `GHTK_STD`. Việc nền chạy ngay được: `logistics.carrier-sync`.

## Chỉ môi trường phát triển

| GET | `/api/dev/sms?to={SĐT}` | Hộp thư của nhà cung cấp SMS giả lập (20 tin mới nhất) — 404 ngoài Development |
|---|---|---|

## API cho ứng dụng di động

Mọi endpoint là REST + JWT, không phụ thuộc phiên máy chủ. App lưu `refreshToken` trong kho bảo mật của thiết bị,
gửi `device` (tên máy) khi đăng nhập để hiện đúng trong "Thiết bị đăng nhập", và gọi `/api/auth/refresh` khi nhận 401.

<!-- Các chương webhook thanh toán / vận chuyển và "API cho ứng dụng di động" bổ sung ở Phase 5, 6, 11 -->

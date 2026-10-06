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

## Chỉ môi trường phát triển

| GET | `/api/dev/sms?to={SĐT}` | Hộp thư của nhà cung cấp SMS giả lập (20 tin mới nhất) — 404 ngoài Development |
|---|---|---|

## API cho ứng dụng di động

Mọi endpoint là REST + JWT, không phụ thuộc phiên máy chủ. App lưu `refreshToken` trong kho bảo mật của thiết bị,
gửi `device` (tên máy) khi đăng nhập để hiện đúng trong "Thiết bị đăng nhập", và gọi `/api/auth/refresh` khi nhận 401.

<!-- Các chương webhook thanh toán / vận chuyển và "API cho ứng dụng di động" bổ sung ở Phase 5, 6, 11 -->

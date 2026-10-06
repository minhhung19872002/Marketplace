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

## Chỉ môi trường phát triển

| GET | `/api/dev/sms?to={SĐT}` | Hộp thư của nhà cung cấp SMS giả lập (20 tin mới nhất) — 404 ngoài Development |
|---|---|---|

## API cho ứng dụng di động

Mọi endpoint là REST + JWT, không phụ thuộc phiên máy chủ. App lưu `refreshToken` trong kho bảo mật của thiết bị,
gửi `device` (tên máy) khi đăng nhập để hiện đúng trong "Thiết bị đăng nhập", và gọi `/api/auth/refresh` khi nhận 401.

<!-- Các chương webhook thanh toán / vận chuyển và "API cho ứng dụng di động" bổ sung ở Phase 5, 6, 11 -->

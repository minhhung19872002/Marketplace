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

<!-- Các chương webhook thanh toán / vận chuyển và "API cho ứng dụng di động" bổ sung ở Phase 5, 6, 11 -->

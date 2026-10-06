# 04 — Cài đặt & vận hành

> Bản đầu (Phase 2). Các chương HTTPS, sao lưu/phục hồi, giám sát bổ sung ở Phase 13.

## Khởi chạy

```bash
cp .env.example .env          # đổi SH_DB_PASSWORD, SH_MINIO_SECRET_KEY, SH_MEILI_MASTER_KEY, SH_JWT_SECRET
docker compose up -d --build
docker compose ps             # mọi dịch vụ "healthy"
curl http://localhost:18000/health/ready
```

Lần chạy đầu API tự áp migration và gieo dữ liệu nền: tham số hệ thống, quyền & vai trò, danh mục hành chính
(10.810 đơn vị), tài khoản quản trị, tài khoản mẫu. Mỗi phần tự kiểm đã có chưa — chạy lại không nhân bản.

## Tài khoản ban đầu

| Tài khoản | Đăng nhập | Ghi chú |
|---|---|---|
| Quản trị cao nhất | `admin` | Phải đổi mật khẩu ở lần đăng nhập đầu (trang `/admin/`) |
| 20 người mua mẫu | `0900000001` … `0900000020` | |
| 3 chủ shop mẫu | `0900000101` … `0900000103` | Shop được tạo ở Phase 3 |
| 2 nhân viên shop mẫu | `0900000201` … `0900000202` | |

Mật khẩu sinh ngẫu nhiên, **chỉ in một lần** ra stdout của API, không ghi vào tệp log hay CSDL:

```bash
docker compose logs api | grep SEED
```

Lỡ mất mật khẩu quản trị: dùng "Quên mật khẩu" với `admin@shophub.local` (thư đến Mailpit `http://localhost:18025`).

## Biến môi trường

Xem chú thích trong `.env.example`. Riêng cho môi trường production: `SH_SEED_SAMPLE=false`, `SH_RATE_LIMIT_AUTH` /
`SH_RATE_LIMIT_OTP` để mặc định (bỏ dòng), `ASPNETCORE_ENVIRONMENT=Production` (tắt Swagger và hộp SMS giả lập).

## Chẩn đoán

| Việc | Lệnh |
|---|---|
| Log API | `docker compose logs -f api` (tệp: volume `apilogs`, `/app/logs`) |
| Cảnh báo/lỗi đã lưu | `SELECT * FROM sys.logs ORDER BY id DESC LIMIT 50;` |
| Serilog không ghi được (sink lỗi) | đặt `SH_SERILOG_SELFLOG=/app/logs/serilog-self.log` rồi khởi động lại API |
| Việc nền (Hangfire) | `/api/admin/jobs` (quyền `SYS.JOB.VIEW`) |
| Tin outbox kẹt | `SELECT type, attempts, last_error FROM sys.outbox_messages WHERE processed_at IS NULL;` |
| SMS giả lập | `GET /api/dev/sms?to=09…` (chỉ Development) |

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

Gateway: `SH_GATEWAY_RATE` / `SH_GATEWAY_BURST` (mặc định 30r/s, burst 60 — giữ nguyên ở production). Chạy bộ e2e trên
stack (mọi trình duyệt cùng một IP): `SH_GATEWAY_RATE=100r/s SH_GATEWAY_BURST=200 docker compose up -d nginx`.

## Đo tải Flash Sale (k6, 1.000 người)

1. Khởi động API với tài khoản tải và giới hạn per-IP nâng (người mua thật đến từ nhiều IP, k6 chỉ có một):
   `SH_SEED_LOAD_USERS=1000 SH_LOAD_USER_PASSWORD=<mật khẩu tự chọn> SH_RATE_LIMIT_AUTH=100000 SH_RATE_LIMIT_GLOBAL=1000000 docker compose up -d api`
   (tài khoản `0970000000`…`0970000999`, có sẵn địa chỉ giao hàng; mật khẩu không ghi vào repo).
2. Cần một khung Flash Sale đang chạy (dữ liệu mẫu có sẵn).
3. `docker run --rm --network shophub_default -v "$PWD/e2e/load:/scripts" -e BASE_URL=http://api:8080 -e LOAD_PASSWORD=<như trên> -e USERS=1000 grafana/k6 run /scripts/flash-sale.js`
4. Đạt khi: không lỗi máy chủ, số suất bán ra = số đơn và không vượt suất, p95 đặt hàng < 3 s.

## Realtime (SignalR)

Hub `/hubs/realtime` chạy trong container API; nhiều bản API dùng chung **Redis backplane** (kênh `shophub:signalr*`)
nên tin phát ở bản này tới client nối vào bản kia. Gateway đã chuyển WebSocket; khi đặt thêm proxy / load balancer phía
trước, cần cho phép `Upgrade` và thời gian chờ đọc ≥ 1 giờ (nếu không, trình duyệt rơi về long polling — vẫn chạy được
nhưng tốn tài nguyên hơn). Dev server Vite của `web/` và `seller/` đã chuyển `/hubs` (ws) tới API.

Email thông báo đi qua Mailpit ở stack dev; push FCM là bản giả lập (ghi log, không gửi).

## Chẩn đoán

| Việc | Lệnh |
|---|---|
| Log API | `docker compose logs -f api` (tệp: volume `apilogs`, `/app/logs`) |
| Cảnh báo/lỗi đã lưu | `SELECT * FROM sys.logs ORDER BY id DESC LIMIT 50;` |
| Serilog không ghi được (sink lỗi) | đặt `SH_SERILOG_SELFLOG=/app/logs/serilog-self.log` rồi khởi động lại API |
| Việc nền (Hangfire) | `/api/admin/jobs` (quyền `SYS.JOB.VIEW`) |
| Tin outbox kẹt | `SELECT type, attempts, last_error FROM sys.outbox_messages WHERE processed_at IS NULL;` |
| SMS giả lập | `GET /api/dev/sms?to=09…` (chỉ Development) |
| Realtime không nhận tin | DevTools → Network → WS `/hubs/realtime` (101); `redis-cli PUBSUB CHANNELS 'shophub:signalr*'` |

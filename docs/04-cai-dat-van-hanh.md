# 04 — Cài đặt & vận hành

> Stack dev/demo: phần đầu. Production (HTTPS, giới hạn tài nguyên, sao lưu/phục hồi, giám sát): từ mục "Triển khai production".

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

## Cổng / hãng thật (VNPay, MoMo, ZaloPay, GHN, GHTK)

1. Đăng ký tài khoản thử: VNPay sandbox (sandbox.vnpayment.vn — `TmnCode`, `HashSecret`), MoMo (developers.momo.vn —
   `partnerCode`, `accessKey`, `secretKey`), ZaloPay (docs.zalopay.vn — `app_id`, `key1`, `key2`; sandbox
   `sb-openapi.zalopay.vn`), GHN (khachhang-dev / 5sao.ghn.dev — `Token`, `ShopId`), GHTK
   (khachhang.ghtklab.com — `Token`, mã đối tác).
2. Điền khoá vào `.env` (mẫu trong `.env.example`) và `SH_CALLBACK_BASE_URL` = địa chỉ công khai mà nhà cung cấp gọi
   được (máy dev: một đường hầm như ngrok trỏ vào gateway `:18000`). Đặt tham số `SITE.PUBLIC_URL` đúng địa chỉ người
   mua mở (trang "quay về" sau cổng).
3. Khai URL gọi lại ở cổng quản trị của nhà cung cấp: VNPay IPN `…/api/payments/webhooks/vnpay`; GHN / GHTK
   `…/api/logistics/webhooks/GHN?token=<SH_GHN_WEBHOOK_TOKEN>` (GHTK tương tự). MoMo nhận `ipnUrl`, ZaloPay nhận `callback_url` theo từng giao dịch.
4. `docker compose up -d api` → log `Seeded … real carrier channel(s)`; trang thanh toán có VNPay / MoMo / ZaloPay, báo giá có
   "Giao Hàng Nhanh" / "Giao Hàng Tiết Kiệm". Tắt một nhà cung cấp: xoá khoá rồi khởi động lại (kênh vẫn còn trong
   bảng nhưng không được đề xuất).
5. Kiểm tay: đặt đơn VNPay bằng thẻ NCB thử của sandbox, xem đơn chuyển "Chờ xác nhận" sau IPN; chuẩn bị hàng với
   GHN → mã vận đơn GHN; đổi trạng thái trên trang thử của hãng → đơn đổi theo (hoặc chờ `logistics.carrier-sync`).
6. **Hình thức tại cổng** (thẻ ATM, thẻ quốc tế, QR, trả góp, mua trước trả sau): VNPay gửi `vnp_BankCode`
   (VNPAYQR / VNBANK / INTCARD), MoMo đổi `requestType` (captureWallet / payWithATM / payWithCC), ZaloPay gửi
   `preferred_payment_method` trong `embed_data`. **Trả góp / mua trước trả sau chỉ hiện khi** đặt
   `SH_ZALOPAY_INSTALLMENT_METHOD` / `SH_ZALOPAY_PAYLATER_METHOD` bằng mã ZaloPay cấp theo hợp đồng, và trả góp chỉ
   bật cho đơn từ tham số `PAYMENT.INSTALLMENT_MIN_AMOUNT` (mặc định ₫3.000.000). Cổng và đối tác tài chính duyệt khoản
   vay — ShopHub không cấp tín dụng.

## Đăng nhập Google (tuỳ chọn)

1. Google Cloud Console → APIs & Services → Credentials → *Create OAuth client ID* loại **Web application**; *Authorized
   JavaScript origins* = địa chỉ site (vd. `https://shophub.example.vn`, dev: `http://localhost:18000`).
2. `.env`: `SH_GOOGLE_CLIENT_ID=<client id>` rồi `docker compose up -d api`. Trang Đăng nhập / Đăng ký hiện nút Google.
3. Máy chủ tự kiểm ID token (chữ ký RS256 theo khoá công bố của Google, `iss`, `aud` = client id, hạn, email đã xác minh);
   CSP của gateway đã cho phép `https://accounts.google.com/gsi/…` (script, khung, style, kết nối).

## Triển khai production

`docker-compose.prod.yml` luôn dùng **chồng lên** tệp gốc. Khác với stack dev: chỉ gateway mở cổng (80 → chuyển hướng
sang 443, 443 HTTPS + HTTP/2, HSTS), không Mailpit (SMTP thật), không cổng giả lập, không dữ liệu mẫu, môi trường
`Production` (tắt Swagger, hộp SMS giả lập), giới hạn tốc độ mặc định của mã, `restart: always`, giới hạn CPU/RAM từng
dịch vụ, log `json-file` xoay vòng (5 × 20 MB), output cache trang người mua 30 s (`SH_OUTPUT_CACHE_SECONDS`).

1. Máy chủ: Docker 24+, cổng 80/443 mở, tên miền trỏ về máy. `.env` đặt **mọi** bí mật (mục "Biến môi trường") và
   `SH_SMTP_HOST`, `SH_SMTP_FROM`, `SH_MEDIA_PUBLIC_URL=https://<miền>/s3` — thiếu là compose dừng kèm thông báo.
2. Chứng chỉ (Let's Encrypt, lần đầu khi gateway chưa chạy):
   ```bash
   docker run --rm -p 80:80 -v "$PWD/deploy/certs-le:/etc/letsencrypt" certbot/certbot certonly --standalone -d <miền>
   cp deploy/certs-le/live/<miền>/fullchain.pem deploy/certs-le/live/<miền>/privkey.pem deploy/certs/
   ```
   Gia hạn (cron hằng tháng) qua webroot mà gateway phục vụ ở `/.well-known/acme-challenge/`:
   `docker run --rm -v "$PWD/deploy/certs-le:/etc/letsencrypt" -v shophub_certbot-www:/var/www/certbot certbot/certbot renew --webroot -w /var/www/certbot`,
   chép lại hai tệp rồi `docker compose -f docker-compose.yml -f docker-compose.prod.yml exec nginx nginx -s reload`.
   `deploy/certs/` nằm trong `.gitignore`.
3. Triển khai: `deploy/scripts/deploy.sh` — build ảnh gắn nhãn theo mã commit, `up -d`, chờ `/health/ready`, rồi **dọn
   ảnh cũ**: giữ bản đang chạy và bản trước (quay lui: `SH_IMAGE_TAG=<nhãn trước> docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d`).
4. Lần đầu: đăng nhập `/admin/` bằng `admin` (mật khẩu in một lần trong `docker compose logs api | grep SEED`), đổi mật
   khẩu, đặt tham số `SITE.PUBLIC_URL`, thông tin pháp nhân, bật cổng thanh toán / hãng vận chuyển thật (mục trên).

`deploy/nginx/gateway-https.conf` sinh từ `gateway.conf` và phải định tuyến y hệt — phép thử
`The_https_gateway_routes_exactly_like_the_dev_gateway` và `NginxConfigParityTests` canh chuyện này.

## Sao lưu & phục hồi

| Dịch vụ (chỉ production) | Làm gì | Ở đâu |
|---|---|---|
| `backup-db` | `pg_dump -Fc` theo `SH_BACKUP_CRON` (mặc định `30 19 * * *` UTC = 02:30 giờ VN), kiểm tệp bằng `pg_restore --list`, xoá bản cũ hơn `SH_BACKUP_KEEP_DAYS` (14) | `./backups/db/shophub-yyyyMMdd-HHmmss.dump` |
| `backup-files` | `mc mirror --watch` mọi bucket MinIO, liên tục | `./backups/minio/<bucket>/` |

Sao lưu ngay: `docker compose -f docker-compose.yml -f docker-compose.prod.yml exec backup-db sh /usr/local/bin/backup-db.sh`.
Chép `./backups` ra ngoài máy (rsync / object storage khác vùng) — bản sao trên cùng đĩa không chống được hỏng đĩa.

Phục hồi: `deploy/scripts/restore.sh backups/db/<tệp>.dump [--files]` — hỏi xác nhận (gõ `dong y`), dừng API,
`pg_restore --clean`, tuỳ chọn chép lại tệp vào MinIO, bật API (API tự dựng lại chỉ mục Meilisearch khi số tài liệu lệch
CSDL). Đã diễn tập trên CSDL dev: 489 đơn, 1.096 sản phẩm, tổng sổ cái 35.146.840 ₫ khớp trước / sau khôi phục.

## Giám sát

| Gì | Ở đâu |
|---|---|
| Sống / sẵn sàng | `GET /health` (tiến trình), `GET /health/ready` (PostgreSQL, Redis, MinIO, Meilisearch) — gắn vào uptime monitor bên ngoài |
| Trạng thái container | `docker compose -f docker-compose.yml -f docker-compose.prod.yml ps` (healthcheck từng dịch vụ) |
| Lỗi ứng dụng | bảng `sys.logs` (mức Warning trở lên), tệp `/app/logs` (volume `apilogs`); OpenTelemetry tuỳ chọn |
| Việc nền | Hangfire (`/api/admin/jobs`, quyền `SYS.JOB.VIEW`): việc lỗi, lần chạy cuối |
| Hàng đợi outbox | câu SQL ở mục Chẩn đoán — tin chưa xử lý tăng dần là dấu hiệu Meilisearch / SMTP hỏng |
| Sao lưu | tệp `.dump` mới nhất trong `./backups/db` không quá 24 giờ |

## Hiệu năng — 1 triệu sản phẩm

Stack riêng (project `shophub-perf`, volume riêng, không đụng dữ liệu dev):

```bash
docker compose -p shophub-perf -f docker-compose.yml -f docker-compose.perf.yml up -d --build   # SH_SEED=perf, SH_PERF_PRODUCTS=1000000
# chờ http://localhost:18100/health/ready (gieo 1 triệu sản phẩm ≈ 4 phút, Meilisearch lập chỉ mục thêm vài phút)
docker run --rm --network shophub-perf_default -v "$PWD/e2e/load:/scripts" -e BASE_URL=http://nginx grafana/k6 run /scripts/perf.js
docker compose -p shophub-perf down -v
```

Kết quả đo (2026-10-07, 1.001.000 sản phẩm, 20 người dùng ảo × 60 s qua gateway, 0 lỗi; ngưỡng mục 6.3):

| Đầu mối | p95 không cache | p95 output cache 30 s | Ngưỡng |
|---|---|---|---|
| Tìm kiếm (từ khoá, trang 1–3) | 131 ms | 19 ms | < 500 ms |
| Tìm kiếm + lọc giá/sao + sắp giá | 110 ms | 18 ms | < 500 ms |
| Danh mục (bán chạy) | 102 ms | 17 ms | < 300 ms |
| Chi tiết sản phẩm | 62 ms | 20 ms | < 300 ms |
| Gợi ý trang chủ | 58 ms | 72 ms | < 300 ms |

Trước khi tối ưu (50.000 sản phẩm): danh mục 332 ms — trượt. Đã sửa: gợi ý trang chủ truy vấn hai pha có trần,
chỉ mục `ix_products_best_selling`, output cache Redis cho các GET công khai của trang người mua.

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
| Cổng / hãng thật không phản hồi | log `VNPay … call failed` / `GHN call … failed`; `SELECT provider, result, count(*) FROM sales.payment_webhook_events GROUP BY 1, 2;` |

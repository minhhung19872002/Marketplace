# ShopHub — Sàn thương mại điện tử đa người bán

Sàn TMĐT C2C + B2C cho thị trường Việt Nam, ba phía: **Người mua** (`web/`), **Kênh Người Bán** (`seller/`),
**Quản trị sàn** (`admin/`), chung một API .NET 8.

Đặc tả và lộ trình: [`PROMPT-BUILD-SHOPHUB.md`](PROMPT-BUILD-SHOPHUB.md). Tiến độ từng phase:
[`docs/07-bang-doi-chieu-chuc-nang.md`](docs/07-bang-doi-chieu-chuc-nang.md).

> **Trạng thái hiện tại: xong Phase 0–13.** Ba phía người mua / Kênh Người Bán / quản trị sàn chạy trên dữ liệu thật: tìm kiếm không dấu có facet,
> giỏ theo shop, PricingEngine, voucher sàn / shop, xu, Flash Sale, checkout tách đơn có idempotency, COD + cổng giả lập (VNPay / MoMo,
> GHN / GHTK bật bằng khoá), máy trạng thái đơn, vận đơn giả lập, đánh giá, trả hàng & khiếu nại, sổ cái kép, giải ngân, Ví ShopHub,
> chat thời gian thực, thông báo đa kênh, quản trị & báo cáo; tài khoản phụ, trang trí & danh mục shop, Excel hàng loạt, Freeship / Voucher
> Xtra, đăng nhập Google (tuỳ chọn); đa kho (mỗi kho gửi một kiện), kênh vận chuyển / COD theo shop, facet vận chuyển & dịch vụ, đăng ký chiến dịch
> của sàn, xuất Excel chạy nền; SEO cho máy thu thập, 1 triệu sản phẩm đạt ngưỡng, 375 px / 1366 px, WCAG AA, production HTTPS +
> sao lưu. `docker compose up -d` có sẵn dữ liệu mẫu: ~1.000 sản phẩm, ~600 đơn trải 90 ngày qua đúng các lệnh nghiệp vụ, 800 đánh giá.
> Hướng dẫn: [người mua](docs/01-huong-dan-nguoi-mua.md) · [người bán](docs/02-huong-dan-nguoi-ban.md) · [quản trị](docs/03-huong-dan-quan-tri.md);
> kịch bản kiểm thử: [`docs/06`](docs/06-kich-ban-kiem-thu.md); chưa làm / ngoài phạm vi: `docs/07` và mục 12 của đặc tả.
> Tài liệu API: [`docs/05-api-reference.md`](docs/05-api-reference.md).

## Cấu trúc

```
backend/   ShopHub.sln — Domain, Application, Infrastructure, Reporting, Api + tests (.NET 8)
web/       Site người mua — React 18 + TypeScript + Vite (@shophub/web)
seller/    Kênh Người Bán — React + TS + Ant Design 5 (@shophub/seller)
admin/     Quản trị sàn — React + TS + Ant Design 5 (@shophub/admin)
e2e/       Playwright (19 kịch bản cũ + kịch bản mục 9) · e2e/load/ k6 (Flash Sale 1.000 người, hiệu năng 1 triệu sản phẩm)
deploy/    nginx/ (gateway, gateway-https, SPA), postgres/init/, scripts/ (deploy, backup-db, restore), frontend.Dockerfile
docs/      Quyết định kỹ thuật, đối chiếu chức năng, sổ lỗi…
```

## Chạy toàn bộ bằng Docker

```bash
cp .env.example .env      # sửa mật khẩu / khoá
docker compose up -d --build
```

| Địa chỉ | Dịch vụ |
|---|---|
| http://localhost:18000/ | Site người mua |
| http://localhost:18000/seller/ | Kênh Người Bán |
| http://localhost:18000/admin/ | Quản trị sàn |
| http://localhost:18000/health | Health check API (sống) |
| http://localhost:18000/health/ready | Trạng thái PostgreSQL, Redis, MinIO, Meilisearch |
| http://localhost:18000/api/site/info | Thông tin sàn (từ tham số hệ thống) |
| http://localhost:18080/swagger | Swagger (môi trường Development) |
| http://localhost:18025/ | Mailpit (thư bắt được khi dev) |
| http://localhost:18901/ | MinIO console |
| http://localhost:18000/s3/… | Ảnh/tệp đã lưu (chỉ đọc) |

Mọi cổng chỉ mở trên `127.0.0.1`, dải 18xxx (xem `docs/00-quyet-dinh-ky-thuat.md`).

**Tài khoản lần đầu:** mật khẩu quản trị (`admin`) và các tài khoản mẫu được sinh ngẫu nhiên, in **một lần** ra
`docker compose logs api | grep SEED`. Quản trị phải đổi mật khẩu ở lần đăng nhập đầu. Chi tiết: [`docs/04-cai-dat-van-hanh.md`](docs/04-cai-dat-van-hanh.md).

## Production

```bash
cp .env.example .env                 # mọi bí mật + SH_SMTP_HOST, SH_SMTP_FROM, SH_MEDIA_PUBLIC_URL
# chứng chỉ vào deploy/certs/{fullchain,privkey}.pem
deploy/scripts/deploy.sh             # build theo mã commit, up -d, chờ sẵn sàng, dọn ảnh cũ (giữ 2 bản)
deploy/scripts/restore.sh backups/db/<tệp>.dump [--files]
```

HTTPS, sao lưu mỗi đêm, giám sát, đo hiệu năng 1 triệu sản phẩm: [`docs/04-cai-dat-van-hanh.md`](docs/04-cai-dat-van-hanh.md).

## Phát triển từng phần

```bash
cd backend && dotnet build && dotnet test
cd web     && npm install && npm run dev        # http://localhost:5173
cd seller  && npm install && npm run dev        # http://localhost:5174/seller/
cd admin   && npm install && npm run dev        # http://localhost:5175/admin/
```

## Kiểm thử

```bash
cd backend && dotnet test                        # unit + quét mã nguồn + tích hợp (Testcontainers, cần Docker)
cd web && npx tsc -b && npx vitest run           # tương tự cho seller/, admin/
cd e2e && npm install && npx playwright install chromium
npx playwright test                              # tự chạy web dev server
SH_E2E_BASE_URL=http://localhost:18000 npx playwright test   # chạy trên stack Docker (+ smoke test)
```

Các kịch bản cần quyền quản trị (duyệt shop, cộng xu, rút ngắn hạn thanh toán) đọc tài khoản từ `SH_E2E_ADMIN_USER` /
`SH_E2E_ADMIN_PASSWORD` (không lưu trong repo); thiếu hai biến này thì các kịch bản đó được bỏ qua.

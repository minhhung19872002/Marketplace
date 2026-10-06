# ShopHub — Sàn thương mại điện tử đa người bán

Sàn TMĐT C2C + B2C cho thị trường Việt Nam, ba phía: **Người mua** (`web/`), **Kênh Người Bán** (`seller/`),
**Quản trị sàn** (`admin/`), chung một API .NET 8.

Đặc tả và lộ trình: [`PROMPT-BUILD-SHOPHUB.md`](PROMPT-BUILD-SHOPHUB.md). Tiến độ từng phase:
[`docs/07-bang-doi-chieu-chuc-nang.md`](docs/07-bang-doi-chieu-chuc-nang.md).

> **Trạng thái hiện tại: xong Phase 0–12** (chuyển đổi repo, nền móng backend, tài khoản, danh mục & sản phẩm, tìm kiếm & trang người mua, giỏ hàng & thanh toán, đơn hàng & vận chuyển, đánh giá & trả hàng / khiếu nại, tài chính: sổ cái, giải ngân, rút tiền, Ví ShopHub, marketing: Flash Sale, combo, mua kèm, quà tặng, banner, chiến dịch, hạng thành viên, điểm danh xu, chat người mua ↔ shop thời gian thực (SignalR), thông báo đa kênh, thông báo hàng loạt, cổng VNPay / MoMo và hãng GHN / GHTK bật bằng khoá trong `.env`, quản trị đủ phân hệ VI: tổng quan, 11 báo cáo có biểu đồ + Excel/PDF, điểm phạt, can thiệp đơn, nội dung & mẫu tin; phân tích cho shop).
> Kênh Người Bán (đăng ký shop, đăng/sửa sản phẩm, tồn kho) và quản trị (duyệt shop/sản phẩm, ngành hàng) chạy thật.
> Trang người mua đọc dữ liệu thật: tìm kiếm không dấu có facet (Meilisearch, dự phòng PostgreSQL), trang danh mục / sản phẩm / shop,
> yêu thích, theo dõi shop, ~1.000 sản phẩm mẫu. Giỏ hàng trên máy chủ, thanh toán tách đơn theo shop với voucher sàn/shop, xu, phí vận
> chuyển giả lập, COD và cổng thanh toán giả lập (`SH_PAYMENT_SIMULATED=true`, chỉ cho demo/kiểm thử). Shop xử lý đơn, in phiếu giao
> PDF; hãng vận chuyển giả lập đẩy hành trình; huỷ / yêu cầu huỷ, tự hoàn thành, thông báo trong ứng dụng.
> Tài liệu API: [`docs/05-api-reference.md`](docs/05-api-reference.md).

## Cấu trúc

```
backend/   ShopHub.sln — Domain, Application, Infrastructure, Reporting, Api + tests (.NET 8)
web/       Site người mua — React 18 + TypeScript + Vite (@shophub/web)
seller/    Kênh Người Bán — React + TS + Ant Design 5 (@shophub/seller)
admin/     Quản trị sàn — React + TS + Ant Design 5 (@shophub/admin)
e2e/       Playwright (19 kịch bản người mua + smoke test stack)
deploy/    nginx/ (gateway + SPA), postgres/init/ (extension), frontend.Dockerfile
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

# 07 — Bảng đối chiếu chức năng

Từng mục của đặc tả → nơi hiện thực → bằng chứng. Cập nhật sau mỗi phase.

## Tiến độ theo phase (mục 11)

| Phase | Nội dung | Trạng thái | Bằng chứng |
|---|---|---|---|
| 0 | Chuyển đổi repo | **Xong** | Xem bảng Phase 0 dưới đây |
| 1 | Nền móng backend | Chưa làm | |
| 2 | Tài khoản | Chưa làm | |
| 3 | Danh mục & sản phẩm | Chưa làm | |
| 4 | Tìm kiếm & trang người mua | Chưa làm | |
| 5 | Giỏ hàng & thanh toán | Chưa làm | |
| 6 | Đơn hàng & vận chuyển | Chưa làm | |
| 7 | Đánh giá, trả hàng, khiếu nại | Chưa làm | |
| 8 | Tài chính | Chưa làm | |
| 9 | Marketing | Chưa làm | |
| 10 | Chat & thông báo thời gian thực | Chưa làm | |
| 11 | Cổng thật | Chưa làm | |
| 12 | Quản trị & báo cáo | Chưa làm | |
| 13 | Hoàn thiện | Chưa làm | |

## Phase 0 — Chuyển đổi repo

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Dời `src/` vào `web/` | `web/src/**` (lịch sử git giữ qua `git mv`) | `git log --follow web/src/App.tsx` |
| Chuyển sang TypeScript | Mọi tệp `.tsx`/`.ts`, `strict: true`, không `any`; kiểu chung ở `web/src/types.ts` | `cd web && npx tsc -b` sạch |
| Giữ nguyên giao diện | Không đổi CSS ngoài tên biến token | 19 e2e xanh |
| Khung `backend/` | `ShopHub.sln`: Domain, Application, Infrastructure, Reporting, Api + UnitTests, IntegrationTests | `cd backend && dotnet build` (0 cảnh báo), `dotnet test` xanh |
| Khung `seller/`, `admin/` | Vite + React + TS + Ant Design 5, gọi `/health` thật | `npm run build` từng gói; trang hiện trạng thái API |
| Dời e2e vào `e2e/` | `e2e/e2e.spec.cjs`, `e2e/playwright.config.cjs` | `cd e2e && npx playwright test` → 19 passed |
| docker-compose đủ dịch vụ | `docker-compose.yml`: postgres, redis, minio (+ minio-init), meilisearch, mailpit, api, web, seller, admin, nginx | `docker compose up -d` → mọi dịch vụ healthy |
| Đổi id `shopee-*` | `mall`, `hang-4-sao`; token `--sh-primary` | `grep -ri shopee web/src` rỗng |

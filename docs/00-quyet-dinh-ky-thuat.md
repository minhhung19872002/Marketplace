# 00 — Sổ quyết định kỹ thuật

Ghi lại mọi chỗ tự chốt khi đặc tả (`PROMPT-BUILD-SHOPHUB.md`) không nói rõ. Mỗi mục: ngày, quyết định, lý do.

| # | Ngày | Quyết định | Lý do |
|---|---|---|---|
| 1 | 2026-10-06 | Cổng mở ra máy chủ dùng dải **18xxx**, chỉ bind `127.0.0.1`: gateway `18000`, API `18080`, PostgreSQL `18432`, Redis `18379`, MinIO `18900/18901`, Meilisearch `18700`, Mailpit `18025` (UI) / `18125` (SMTP). | Máy dev đang chạy nhiều dự án chiếm 80, 5432, 6379, 8080…; dải riêng tránh đụng. |
| 2 | 2026-10-06 | Backend target **net8.0** (đúng đặc tả). Máy dev có SDK 9 build được net8; Docker build bằng `mcr.microsoft.com/dotnet/sdk:8.0`. Không ghim `global.json`. | SDK 9 hỗ trợ target net8.0; ghim SDK 9 sẽ làm hỏng build trong image SDK 8. |
| 3 | 2026-10-06 | `Directory.Build.props` bật `TreatWarningsAsErrors` cho toàn solution. | Mục 8: "build sạch không cảnh báo". |
| 4 | 2026-10-06 | Ba SPA (`web`, `seller`, `admin`) dùng chung `deploy/frontend.Dockerfile` (Node build → Nginx tĩnh). Gateway Nginx cắt tiền tố `/seller/`, `/admin/` trước khi chuyển; `seller`/`admin` build với `base: '/seller/'`, `'/admin/'`. | Một Dockerfile, ít lệch cấu hình; SPA fallback xử lý trong container. |
| 5 | 2026-10-06 | Gateway dùng `resolver 127.0.0.11` + `set $upstream …` + `proxy_pass $upstream` (không khối `upstream`). Trang lỗi 429/502/503/504 trả **JSON tiếng Việt**. | Mục 6.1, 7: không ghim IP, không trả trang HTML 503. |
| 6 | 2026-10-06 | Token màu đổi `--shopee-orange` → `--sh-primary`, `--shopee-orange-dark` → `--sh-primary-dark`; id lối tắt `shopee-mall` → `mall`, `shopee-4sao` → `hang-4-sao`; bỏ chữ "Shopee" khỏi mã và chú thích. | Phần A — quy định thương hiệu. |
| 7 | 2026-10-06 | Phase 0 **giữ dữ liệu giả** (`web/src/data/products.ts`) và các Context `localStorage`; thay bằng API ở Phase 2 (đăng nhập), Phase 4 (sản phẩm, tìm kiếm), Phase 5 (giỏ, thanh toán). | Đúng thứ tự mục 11; Phase 0 chỉ chuyển đổi repo, giữ 19 e2e xanh. |
| 8 | 2026-10-06 | `formatPrice`/`formatSold` chuyển vào `web/src/lib/money.ts`, `removeTones` vào `web/src/lib/text.ts`; `data/products.ts` re-export để các trang cũ không phải đổi import ngay. | Mục 3.1 yêu cầu `lib/money.ts`. |
| 9 | 2026-10-06 | e2e đọc `SH_E2E_BASE_URL`; không đặt thì Playwright tự chạy `npm run dev` của `web` (cổng 5173). Đặt `SH_E2E_BASE_URL=http://localhost:18000` để chạy trên stack Docker. | Cùng bộ test chạy được cả dev lẫn stack đầy đủ. |
| 10 | 2026-10-06 | Bucket `sh-products`, `sh-reviews`, `sh-banners` cho đọc công khai; `sh-kyc`, `sh-chat` riêng tư (chỉ URL ký có hạn). Tạo bằng dịch vụ một lần `minio-init`. | Mục 0.1, 6.1. |

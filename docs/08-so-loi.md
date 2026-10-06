# 08 — Sổ lỗi

Ghi thẳng, có bằng chứng. Mỗi lỗi: mã, ngày, mô tả, nguyên nhân, cách sửa, phép thử bắt lỗi.

| Mã | Ngày | Mô tả | Nguyên nhân | Sửa | Phép thử |
|---|---|---|---|---|---|
| L001 | 2026-10-06 | "Xem tất cả" Flash Sale luôn ra 0 kết quả | Link `/tim-kiem?q=flash-sale` không khớp tên sản phẩm nào | Đổi sang `?sort=discount` | Kiểm thủ công + Playwright tạm (commit `3d204f5`) |
| L002 | 2026-10-06 | Đổi danh mục qua URL khi đang ở trang tìm kiếm không cập nhật bộ lọc | `selectedCats` chỉ khởi tạo lúc mount | `useEffect` theo `category` | như trên |
| L003 | 2026-10-06 | Gợi ý tìm kiếm ở header không bỏ dấu | Dùng `toLowerCase` thay vì `removeTones` | Dùng chung `removeTones` | như trên |
| L004 | 2026-10-06 | Số lượng trong giỏ vượt tồn kho | `updateQuantity` không chặn trên | `clampQty` trong `CartContext` | như trên |
| L005 | 2026-10-06 | Reload trang đặt hàng thành công mất tổng tiền | Chỉ đọc `location.state` | Lưu `sessionStorage` | như trên |
| L006 | 2026-10-06 | Danh mục chọn qua URL nằm ngoài top 10 không hiện ô tích ở sidebar | Sidebar chỉ render 10 mục đầu | Luôn hiện mục từ URL/đang chọn + nút "Thêm" | Playwright tạm (commit `41cdc4a`) |
| L007 | 2026-10-06 | `deploy/nginx/spa.conf` thiếu `limit_req_status 429`, `resolver`, trang lỗi JSON | Viết trước khi có luật chung | Bổ sung đủ bộ cấu hình chung | `NginxConfigParityTests` đỏ → xanh |
| L008 | 2026-10-06 | Màu viết thẳng trong TSX (`Banner.tsx` gradient, icon SVG `#fff`/`#999` ở `Header.tsx`, ảnh dự phòng ở `MallBrands.tsx`) | Mã kế thừa từ bản giả | Chuyển sang biến CSS / `currentColor` / `data/products.ts` | `palette.test.ts` đỏ trên bản cũ → xanh |
| L009 | 2026-10-06 | Đếm ngược Flash Sale theo giờ máy khách (`getHours`) | Mã kế thừa | `lib/datetime.secondsToNextSlot` theo giờ Việt Nam | `datetime.test.ts` |
| L010 | 2026-10-06 | Đường dẫn API không tồn tại trả 401 thay vì 404 | `FallbackPolicy` áp cả khi không khớp endpoint | Bỏ fallback, dựa vào `EndpointAuthorisationTests` | `Unknown_route_is_a_json_404` |
| L011 | 2026-10-06 | Sink Serilog → PostgreSQL **chưa từng ghi được dòng nào** từ Phase 1 | (1) `Serilog.Sinks.PostgreSQL` 2.1.0 gọi `NpgsqlBinaryImporter.Complete()` kiểu cũ → `MissingMethodException` ở Npgsql 8; (2) bộ ghi thời gian gửi `DateTimeOffset` +07:00, Npgsql 8 chỉ nhận UTC. Cả hai lỗi bị nuốt trong SelfLog. | `useCopy: false` + `UtcTimestampColumnWriter`; thêm `SH_SERILOG_SELFLOG` để chẩn đoán | `Seeded_credentials_never_reach_the_log_table` (có đối chứng dương: cảnh báo đã biết phải xuất hiện trong `sys.logs`) |
| L012 | 2026-10-06 | Mật khẩu quản trị khởi tạo ghi vào tệp log và bảng `sys.logs` | Gieo dữ liệu dùng `logger.LogWarning` | In ra stdout (`[SEED]`), không qua Serilog; xoá dòng đã lọt ở môi trường dev | như trên — đỏ khi dùng logger, xanh sau khi sửa |
| L013 | 2026-10-06 | E2E không nhận được OTP trên stack | Giới hạn 10 OTP/phút/IP (mọi test chung IP) + helper không kiểm phản hồi nên lỗi 429 bị che; thêm vào đó regex trong test bị chèn ký tự backspace (0x08) | Giới hạn cấu hình được (`SH_RATE_LIMIT_*`), helper kiểm `res.ok()`, sửa regex | 24 → 26 e2e xanh trên stack |
| L014 | 2026-10-06 | Nhật ký thao tác ghi IP dạng `::ffff:192.168.x.x` lẫn `192.168.x.x` | IPv4 ánh xạ sang IPv6 khi gọi thẳng Kestrel | Chuẩn hoá về IPv4 trong `HttpCurrentUser` | kiểm thủ công trang Nhật ký |
| L015 | 2026-10-06 | Sửa sản phẩm có thêm phân loại → 500 | Khoá `Id` khai "do CSDL sinh"; EF phát UPDATE cho SKU/lựa chọn mới vì Id đã có giá trị | `ValueGeneratedNever()` cho mọi khoá `Entity` | `Editing_keeps_sku_identity_and_sensitive_edits_need_re_review` (đỏ → xanh) |
| L016 | 2026-10-06 | `POST …/products/{id}/actions/{action}` luôn 404 | `action` là tham số route dành riêng của MVC | Đổi thành `{operation}` | `Two_tier_product_lifecycle_from_draft_to_selling` |
| L017 | 2026-10-06 | E2E làm khoá tạm tài khoản `admin` thật | Test "sai mật khẩu" gõ sai cho chính `admin`; sau 5 lần chạy, cơ chế khoá (đúng thiết kế) kích hoạt | Test dùng tài khoản thử riêng | `admin.spec.cjs` |
| L018 | 2026-10-06 | Sửa nhanh giá/tồn báo "thiếu giá" với sản phẩm có SKU đã tắt | Chỉ đưa SKU đang bán vào bảng dựng lại | Đưa mọi SKU của tổ hợp còn hiệu lực, giữ cờ bật/tắt | `Quick_edit_validates_through_the_domain` |

> L001–L006 sửa trước khi có quy trình "đỏ trước, xanh sau" của mục 8; từ Phase 1 mọi lỗi phải kèm phép thử
> đỏ trước khi sửa và nằm lại trong bộ test.

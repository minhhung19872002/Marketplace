# Tiến độ G-VIS vòng 2 + G5

Thứ tự: G-VIS vòng 2 (anh Hùng gửi trực tiếp) → G5. Mục trùng làm một lần, ghi chéo mã.
Cập nhật mỗi khi xong một mục. Trạng thái: ⬜ chưa · 🔄 đang làm · ✅ xong · ⏸ chờ người dùng.

## Trạng thái đầu
- ✅ CI của b8c5b7f (run 37954396917): xanh cả 5 job (backend, web, seller, admin, e2e).

## G-VIS vòng 2 (`docs/plans/G-VIS-2.md`)
| Mục | Nội dung | Trùng G5 | Trạng thái |
|---|---|---|---|
| V2-1 | Giá gạch chỉ khi giá gốc > giá bán (cả khoảng giá) + unit + e2e quét 50 SP | | 🔄 mã xong, chờ kiểm trên stack |
| V2-2 | Logo chữ ngang đầy đủ tên cho mọi shop, không cắt; dùng ở Mall, trang shop, khối shop trang SP | | 🔄 mã xong, chờ kiểm trên stack |
| V2-3 | Ưu đãi ô Mall vừa 1 dòng (rút gọn) | | 🔄 mã xong, chờ kiểm trên stack |
| V2-4 | Feed: 1 tin / mẫu / trang, không 2 ô liền cùng ngành (gợi ý, đã xem, có thể bạn thích, Flash Sale) | | 🔄 mã xong, chờ kiểm trên stack |
| V2-5 | Thanh "GỢI Ý HÔM NAY" chỉ dính trong section | | 🔄 mã xong, chờ kiểm trên stack |
| V2-6 | Tìm kiếm hàng đầu: luôn "Bán x+ / tháng"; chỉ ảnh thường (bộ sinh: ảnh bìa luôn đứng đầu) | | 🔄 mã xong, chờ kiểm trên stack |
| V2-7 | Banner phụ 2 không trùng ý "Freeship ₫30K" với hero | | 🔄 mã xong, chờ kiểm trên stack |
| V2-8 | Chữ thanh Flash Sale một màu, đủ tương phản mọi mức tô | G5 B1 | 🔄 mã xong, chờ kiểm trên stack |
| V2-9 | Gallery ≥ 4 ảnh (bìa + trim + 2 crop cận); chip Màu có ảnh/màu | | 🔄 mã xong, chờ kiểm trên stack |
| V2-10 | "Sản phẩm đã xem" < 6 ô giãn đều / < 3 thì ẩn | | 🔄 mã xong, chờ kiểm trên stack |
| V2-11 | Mobile 390: chụp + ghép + chấm điểm trang chủ, chi tiết, danh mục, giỏ, thanh toán | | ⬜ |
| V2-12 | Đủ test, Lighthouse trang chủ ≥ 85, ảnh `.ui-review/vis2/`, push + báo CI | | ⬜ |

## G5 (`docs/plans/G5.md`)
| Mục | Nội dung | Trạng thái |
|---|---|---|
| G5-0 | CI b8c5b7f | ✅ xanh |
| G5-A | Đồng bộ G-VIS ra mọi trang, một component thẻ (luật quét) | ⬜ |
| G5-B1 | = V2-8 (L183) | 🔄 |
| G5-B2 | Banner bản mobile (`<picture>`), trường ảnh mobile ở quản trị | ⬜ |
| G5-B3 | Chốt chính sách in hoa trong docs/00 + luật quét khớp | ⬜ |
| G5-B4 | Ẩn "Đã bán 0" | ⬜ |
| G5-B5 | Đo việc nền demo sau 24 h, đặt trần đơn/sản phẩm và tổng, ghi docs/04 | ⬜ |
| G5-B6 | Seller/admin: theme cam mới, tương phản sáng/tối | ⬜ |
| G5-C | Tự kiểm mọi route web/seller/admin ở 375 & 1440 sau đăng nhập, bảng ở docs/07 | ⬜ |
| G5-D1 | docs/09-kich-ban-trinh-dien.md (10 phút, không ghi mật khẩu) | ⬜ |
| G5-D2 | Bảng "Đi vào chạy thật" docs/04 + đề xuất Flutter vs chạy thật (ước lượng, không tự làm) | ⬜ |
| G5-D3 | Nhắc việc anh tự làm | ⬜ |

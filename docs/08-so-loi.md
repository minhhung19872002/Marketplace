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

> L001–L006 sửa trước khi có quy trình "đỏ trước, xanh sau" của mục 8; từ Phase 1 mọi lỗi phải kèm phép thử
> đỏ trước khi sửa và nằm lại trong bộ test.

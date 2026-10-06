# ShopHub - Sàn thương mại điện tử (Shopee clone)

Trang sàn TMĐT mô phỏng [shopee.vn](https://shopee.vn), dựng theo cùng stack và convention của dự án `AmThucSaiGon` (React 18 + Vite, react-router v6, mỗi component 1 file `.jsx` + `.css`, test Playwright).

## Tính năng

- **Header** cam đặc trưng Shopee: thanh top, ô tìm kiếm, từ khóa hot, giỏ hàng có badge số lượng.
- **Trang chủ**: banner carousel tự chạy, lưới danh mục (18 ngành hàng), **Flash Sale** với đồng hồ đếm ngược + thanh tiến trình, lưới "Gợi ý hôm nay".
- **Chi tiết sản phẩm**: ảnh, giá/giảm giá, chọn số lượng, Thêm vào giỏ / Mua ngay, sản phẩm tương tự.
- **Giỏ hàng**: cập nhật số lượng, xóa, tính tổng tiền, lưu `localStorage` (giữ giữa các lần load).
- **Tìm kiếm & lọc**: tìm không dấu (cả ô gợi ý ở header), lọc theo danh mục / khoảng giá / đánh giá, sắp xếp (liên quan, mới nhất, bán chạy, giá tăng/giảm).
- **Phân loại hàng** (size/màu/dung lượng): bắt buộc chọn trước khi thêm giỏ; số lượng giới hạn theo tồn kho.
- **Thanh toán**: địa chỉ nhận hàng, phương thức vận chuyển, voucher (`SHOPHUB50`, `FREESHIP`, `SALE12`), phương thức thanh toán → trang đặt hàng thành công.
- **Đăng nhập / Đăng ký** giả lập, **Yêu thích**, **Thông báo**, **Trang Shop**.
- Responsive desktop / tablet / mobile.

> Dữ liệu là mock (`src/data/products.js`), **không cần backend**. Ảnh sản phẩm lấy từ CDN demo (dummyjson); khi lỗi mạng sẽ tự đổi sang ảnh SVG data-URI dự phòng.
> Giỏ hàng, yêu thích, tài khoản lưu ở `localStorage`.

## Chạy dev

```bash
npm install
npm run dev            # http://localhost:5173
```

## Test

```bash
npx playwright install chromium   # lần đầu
npm run test:e2e                  # 19 test e2e
```

## Cấu trúc

```
src/
  components/   Header, Footer, Banner, CategoryShortcuts, CategoryGrid, FlashSale,
                MallBrands, ProductCard, ProductGrid, BackToTop, ScrollToTop
  pages/        HomePage, ProductDetail, CartPage, Checkout, OrderSuccess, SearchResults,
                Login, Register, Wishlist, Notifications, ShopPage
  context/      CartContext, WishlistContext, AuthContext (lưu localStorage)
  data/         products.js (mock data + helpers), images.js (URL ảnh sản phẩm)
```

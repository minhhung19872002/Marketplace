# ShopHub - Sàn thương mại điện tử (Shopee clone)

Trang sàn TMĐT mô phỏng [shopee.vn](https://shopee.vn), dựng theo cùng stack và convention của dự án `AmThucSaiGon` (React 18 + Vite, react-router v6, mỗi component 1 file `.jsx` + `.css`, test Playwright).

## Tính năng

- **Header** cam đặc trưng Shopee: thanh top, ô tìm kiếm, từ khóa hot, giỏ hàng có badge số lượng.
- **Trang chủ**: banner carousel tự chạy, lưới danh mục (18 ngành hàng), **Flash Sale** với đồng hồ đếm ngược + thanh tiến trình, lưới "Gợi ý hôm nay".
- **Chi tiết sản phẩm**: ảnh, giá/giảm giá, chọn số lượng, Thêm vào giỏ / Mua ngay, sản phẩm tương tự.
- **Giỏ hàng**: cập nhật số lượng, xóa, tính tổng tiền, lưu `localStorage` (giữ giữa các lần load).
- **Tìm kiếm & lọc**: tìm không dấu, lọc theo danh mục, sắp xếp (mới nhất, bán chạy, giá tăng/giảm).
- Responsive desktop / tablet / mobile.

> Dữ liệu là mock (`src/data/products.js`), ảnh dùng SVG data-URI inline nên **không cần backend / không phụ thuộc mạng**.

## Chạy dev

```bash
npm install
npm run dev            # http://localhost:5173
```

## Test

```bash
npx playwright install chromium   # lần đầu
npm run test:e2e                  # 11 test e2e
```

## Cấu trúc

```
src/
  components/   Header, Footer, Banner, CategoryGrid, FlashSale, ProductCard, ProductGrid
  pages/        HomePage, ProductDetail, CartPage, SearchResults
  context/      CartContext (giỏ hàng + localStorage)
  data/         products.js (mock data)
```

# Catalogue gieo mẫu

Dựng dữ liệu cho `ShopHub.Infrastructure/Seed/Data/` (quyết định #180 trong `docs/00`):

| Tệp | Việc |
|---|---|
| `models.py` | 188 mẫu sản phẩm: mã ảnh dummyjson, danh mục lá, tên tiếng Việt, giá, phân loại, thuộc tính |
| `build.py` | Tải ảnh, đặt lên nền trắng, cắt vuông 800 px, WebP → `ProductImages/`; ghi `product-models.json`; thêm danh mục lá mới vào `catalog-seed.json` |
| `art.py` | Logo + ảnh bìa 30 shop, 3 banner trang chủ, popup, banner chiến dịch → `Art/` (vẽ từ ảnh sản phẩm, phông Be Vietnam Pro) |

```bash
pip install pillow
python backend/tools/seed-catalog/build.py
python backend/tools/seed-catalog/art.py
```

Ảnh tải về được giữ ở `.cache/` (không commit) nên chạy lại không tải lại.

## Nguồn ảnh

- Ảnh sản phẩm: bộ ảnh demo của [dummyjson.com](https://dummyjson.com) (dữ liệu giả lập công khai, dùng cho thử nghiệm / demo).
  Tên thương hiệu thật xuất hiện trong ảnh và tên mẫu chỉ để dữ liệu mẫu trông thật, không phải hàng thật được bán.
- Phông chữ: [Be Vietnam Pro](https://fonts.google.com/specimen/Be+Vietnam+Pro) (SIL Open Font License), chỉ dùng để vẽ ảnh banner / logo.

Danh mục không có ảnh đúng loại (Mẹ & Bé, Đồ Chơi, Máy Ảnh) cố ý không có sản phẩm mẫu — không dùng ảnh sai loại.

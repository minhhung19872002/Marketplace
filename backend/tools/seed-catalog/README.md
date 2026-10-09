# Catalogue gieo mẫu

Dựng dữ liệu cho `ShopHub.Infrastructure/Seed/Data/` (quyết định #180 trong `docs/00`):

| Tệp | Việc |
|---|---|
| `models.py` | `MODELS`: 188 mẫu từ ảnh dummyjson; `OPEN_MODELS` (mã 195 trở đi): mẫu dùng ảnh giấy phép mở; `BRANDS`: thương hiệu của mẫu cũ. Mỗi mẫu: danh mục lá, tên tiếng Việt, giá, phân loại, thuộc tính, xuất xứ, thương hiệu |
| `photos.json` | Ảnh của từng mẫu `OPEN_MODELS`: trang nguồn, URL tệp, tác giả, giấy phép, khung cắt (nếu có) |
| `find-photos.py` | Tìm ảnh trên Wikimedia Commons / Openverse (chỉ CC0, public domain, CC BY, CC BY-SA), dựng bảng ảnh đánh số trong `.cache/sheets/`, `pick` ghi ảnh đã chọn vào `photos.json` |
| `CREDITS.md` | Ghi công từng ảnh giấy phép mở (do `build.py` sinh từ `photos.json`) |
| `build.py` | Tải ảnh, đặt lên nền trắng, cắt vuông 800 px, WebP → `ProductImages/` (chỉ tạo ảnh còn thiếu, không ghi đè ảnh có sẵn, giữ ảnh bìa `pNNN-m.webp`); ghi `product-models.json`; thêm danh mục lá + thương hiệu mới vào `catalog-seed.json`; sinh `CREDITS.md` |
| `art.py` | Logo + ảnh bìa 30 shop, 3 banner trang chủ, popup, banner chiến dịch → `Art/` (vẽ từ ảnh sản phẩm, phông Be Vietnam Pro) |

```bash
pip install pillow
python backend/tools/seed-catalog/build.py      # dummyjson + ảnh giấy phép mở (photos.json)
python backend/tools/seed-catalog/art.py
```

Ảnh tải về được giữ ở `.cache/` (không commit) nên chạy lại không tải lại.

## Nguồn ảnh

- Ảnh sản phẩm mã 1–194: bộ ảnh demo của [dummyjson.com](https://dummyjson.com) (dữ liệu giả lập công khai, dùng cho thử nghiệm / demo).
- Ảnh sản phẩm mã 195 trở đi: ảnh giấy phép mở từ Wikimedia Commons / Openverse (CC0, public domain, CC BY, CC BY-SA —
  không nhận NC / ND); nguồn, tác giả, giấy phép và thay đổi của **từng ảnh** ở [`CREDITS.md`](CREDITS.md). Trang hiển thị
  sản phẩm dùng các ảnh CC BY / CC BY-SA phải giữ được ghi công này (ví dụ trang "Nguồn ảnh" / chân trang dữ liệu mẫu).
  Tên thương hiệu thật xuất hiện trong ảnh và tên mẫu chỉ để dữ liệu mẫu trông thật, không phải hàng thật được bán.
- Phông chữ: [Be Vietnam Pro](https://fonts.google.com/specimen/Be+Vietnam+Pro) (SIL Open Font License), chỉ dùng để vẽ ảnh banner / logo.

Mọi ngành cấp 1 có ít nhất 8 mẫu (Mẹ & Bé, Đồ Chơi, Máy Ảnh & Quay Phim nay có mẫu từ ảnh giấy phép mở) — vẫn không dùng ảnh sai loại.

Thêm mẫu mới: `python find-photos.py search "từ khoá"` → xem `.cache/sheets/<tên>.png` → `python find-photos.py pick <mã> <tên> <số ảnh>…`
→ thêm dòng `m(<mã>, …)` vào `OPEN_MODELS` → `python build.py`.

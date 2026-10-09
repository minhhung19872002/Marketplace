# Catalogue gieo mẫu

Dựng dữ liệu cho `ShopHub.Infrastructure/Seed/Data/` (quyết định #180 trong `docs/00`):

| Tệp | Việc |
|---|---|
| `models.py` | `MODELS`: 188 mẫu từ ảnh dummyjson; `OPEN_MODELS` (mã 195 trở đi): mẫu dùng ảnh giấy phép mở; `BRANDS`: thương hiệu của mẫu cũ. Mỗi mẫu: danh mục lá, tên tiếng Việt, giá, phân loại, thuộc tính, xuất xứ, thương hiệu |
| `photos.json` | Ảnh của từng mẫu `OPEN_MODELS`: trang nguồn, URL tệp, tác giả, giấy phép, khung cắt (nếu có) |
| `find-photos.py` | Tìm ảnh trên Wikimedia Commons / Openverse (chỉ CC0, public domain, CC BY, CC BY-SA), dựng bảng ảnh đánh số trong `.cache/sheets/`, `pick` ghi ảnh đã chọn vào `photos.json` |
| `CREDITS.md` | Ghi công từng ảnh giấy phép mở (do `build.py` sinh từ `photos.json`) |
| `build.py` | Tải ảnh, đóng khung bằng `photo.py`, WebP 800 px → `ProductImages/` (chỉ tạo ảnh còn thiếu; `--reprocess` làm lại mọi ảnh từ bản gốc trong `.cache/`, không bao giờ từ WebP đã xử lý; giữ ảnh bìa quảng cáo `pNNN-ad.webp`); ghi vật đã tách nền vào `.cache/cutouts/`; ghi `product-models.json`; thêm danh mục lá + thương hiệu mới vào `catalog-seed.json`; sinh `CREDITS.md`; ảnh danh mục cấp 1 `Art/cat-<slug>.webp` (168 px, nền trong suốt) |
| `photo.py` | Đóng khung ảnh sản phẩm (G-VIS): cắt sát vật, vật chiếm ~88 % khung vuông, nền trắng hoặc màu nhạt theo ngành (ảnh tách nền); ảnh chụp cảnh thật thì cắt vuông lấp đầy khung; mỗi ảnh ≤ ~80 KB |
| `art-html.mjs` | Vẽ bằng HTML/CSS + Chromium (Playwright của `e2e/`): ảnh bìa quảng cáo ~40 % mẫu (9 mẫu thiết kế), 6 banner chính 797×235 (vẽ 2×), 2 banner phụ, dải 3 banner, banner ShopHub Mall, popup + banner + khung 10.10 → `.cache/art-png/` (`ART_ONLY=<regex>` để vẽ thử một phần) |
| `art-png-to-webp.py` | PNG → `Art/*.webp` (banner ≤ 150 KB) và `ProductImages/pNNN-ad.webp` (≤ 90 KB, đặt đầu danh sách ảnh của mẫu) |
| `art.py` | Logo 30 shop (`--logos`: chữ thương hiệu), ảnh bìa 30 shop → `Art/` (Pillow, phông Be Vietnam Pro) |

```bash
pip install pillow
pip install numpy scipy                          # photo.py
python backend/tools/seed-catalog/build.py      # dummyjson + ảnh giấy phép mở (photos.json); --reprocess: làm lại mọi ảnh
node backend/tools/seed-catalog/art-html.mjs     # ảnh bìa quảng cáo, banner (cần web/node_modules và e2e/node_modules)
python backend/tools/seed-catalog/art-png-to-webp.py
python backend/tools/seed-catalog/art.py --logos # chỉ khi đổi logo shop
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

## Quảng cáo trên ảnh và banner (G-VIS)

Chỉ học bố cục / cỡ / mật độ của các sàn lớn, không dùng logo, ảnh, phông hay tên chương trình của họ. Mọi lời hứa vẽ trên
ảnh phải đúng với dữ liệu mẫu: mã sàn `SHOPHUB50` (giảm ₫50.000 đơn từ ₫250.000), `FREESHIP` (phí ship tối đa ₫30.000),
`SALE12` (12 % tối đa ₫100.000 đơn từ ₫500.000), Flash Sale giảm 10–50 %, trả hàng trong 15 ngày, COD, chiến dịch 10.10
(banner 10.10 hết hạn cùng chiến dịch). Ảnh bìa quảng cáo không ghi giá, không hứa giảm giá riêng cho sản phẩm; nhãn thương
hiệu ở góc là thương hiệu của mẫu (không có thì "ShopHub") — không thể dùng logo shop vì một mẫu được nhiều shop đăng bán.

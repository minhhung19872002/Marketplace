# 09 — Kiểm tra trước khi mở bán

Danh sách này dùng trước khi chuyển ShopHub từ **bản trình diễn** sang **bán thật**. Phần lớn các mục được hệ thống tự
đo — Quản trị → **Hệ thống → Kiểm tra trước khi mở bán** (`GET /api/admin/launch-checklist`, quyền xem tham số). Mục
"chặn" chưa đạt thì **chưa được mở bán**. Phần cuối là các việc phải làm tay (pháp lý, con người), máy không đo được.

## 1. Chế độ chạy `SITE.MODE`

| Giá trị | Ý nghĩa |
|---|---|
| `demo` (mặc định) | Băng "Bản trình diễn" trên trang người mua; cổng thanh toán giả lập dùng được; Flash Sale tự mở khung **và tự điền sản phẩm mẫu** |
| `live` | Không băng; **ẩn cổng thanh toán giả lập**; Flash Sale chỉ tự mở khung, shop tự đăng ký sản phẩm |

Đổi ở Quản trị → Tham số hệ thống → nhóm SITE. Chỉ nhận `demo` hoặc `live`; giá trị khác bị từ chối. Mặc định là
`demo` để một bản cài mới không vô tình nhận tiền thật qua cổng giả lập — chuyển sang `live` là một bước có chủ đích và là
mục đầu tiên của danh sách.

## 2. Các mục hệ thống tự đo

| Mã | Nhóm | Mục | Chặn |
|---|---|---|---|
| `site-mode` | Chế độ | `SITE.MODE = live` | Có |
| `public-url` | Chế độ | `SITE.PUBLIC_URL` là HTTPS, không phải localhost | Có |
| `legal` | Pháp lý | Tên pháp nhân, địa chỉ, mã số thuế, giấy phép không còn giá trị mẫu | Có |
| `moit` | Pháp lý | `SITE.MOIT_URL` trỏ tới trang thông báo / đăng ký trên online.gov.vn | Có |
| `legal-pages` | Pháp lý | Có trang Điều khoản, Quy chế hoạt động, Chính sách bảo mật | Có |
| `payment` | Thanh toán & vận chuyển | Có ít nhất một cổng online thật (VNPay / MoMo / ZaloPay) | Không (bán COD vẫn được) |
| `carrier` | Thanh toán & vận chuyển | Có hãng vận chuyển thật đang bật (GHN / GHTK) | Có |
| `carrier-sim` | Thanh toán & vận chuyển | Đã tắt kênh vận chuyển giả lập | Có |
| `sms` | Thông báo | `SH_SMS_PROVIDER` không phải `simulated` (OTP thật) | Có |
| `smtp` | Thông báo | `SH_SMTP_HOST` không phải hộp thư thử (mailpit / localhost) | Có |
| `admin-password` | Tài khoản & dữ liệu | Đã đổi mật khẩu `admin` ban đầu | Có |
| `sample-data` | Tài khoản & dữ liệu | Không còn tài khoản mẫu `0900000…` | Có |
| `fees` | Tài khoản & dữ liệu | Có biểu phí sàn đang hiệu lực | Có |
| `backup` | Vận hành | Sao lưu tự động thành công trong 26 giờ qua | Có |

## 3. Việc làm tay (máy không đo được)

- [ ] Đăng ký / thông báo website TMĐT với Bộ Công Thương (online.gov.vn) — sàn giao dịch TMĐT phải **đăng ký**; dán đường
      dẫn vào `SITE.MOIT_URL`.
- [ ] Luật sư rà Điều khoản sử dụng, Quy chế hoạt động sàn, Chính sách bảo mật & xử lý dữ liệu cá nhân (Nghị định
      13/2023/NĐ-CP), Chính sách trả hàng; sửa ở Quản trị → Nội dung & mẫu tin.
- [ ] Hợp đồng và khoá thật với cổng thanh toán, hãng vận chuyển, nhà cung cấp SMS brandname, SMTP (docs/04 mục "Cổng /
      hãng thật"); chạy thử một đơn thật nhỏ qua từng cổng, hoàn tiền thử.
- [ ] Cài đặt mới **không** có `SH_SEED_SAMPLE=true` (dữ liệu mẫu chỉ cho demo); tạo tài khoản quản trị theo người thật,
      phân vai trò (Quản trị → Vai trò & quyền), tắt tài khoản không dùng.
- [ ] Đổi mọi bí mật trong `.env` so với bản demo (`SH_JWT_SECRET`, `SH_DATA_KEY`, mật khẩu CSDL, MinIO, Meilisearch).
      **Lưu `SH_DATA_KEY` ở nơi an toàn**: mất khoá là mất số tài khoản ngân hàng, CCCD đã mã hoá.
- [ ] HTTPS và chứng chỉ tự gia hạn (docs/04 mục "Triển khai production").
- [ ] Chép `./backups` ra ngoài máy chủ (rsync / object storage khác vùng); chạy **diễn tập phục hồi** (mục 4) và lưu kết quả.
- [ ] Giám sát: `/health/ready` gắn vào công cụ theo dõi, có người nhận cảnh báo; Quản trị → Việc nền không có việc lỗi.
- [ ] Biểu phí theo ngành, chính sách điểm phạt, hạn trả hàng, ngưỡng COD (Tham số hệ thống) đã được kinh doanh duyệt.
- [ ] Đội vận hành biết quy trình duyệt shop, duyệt sản phẩm, xử lý khiếu nại, duyệt rút tiền (docs/03).

## 4. Diễn tập sao lưu & phục hồi

```bash
# trên máy chủ, ở thư mục ứng dụng
SH_COMPOSE="docker compose -f docker-compose.yml -f docker-compose.vm.yml" deploy/scripts/backup-drill.sh
```

Script sao lưu ngay (`pg_dump -Fc`, kiểm bằng `pg_restore --list`), phục hồi vào CSDL tạm `shophub_drill` cạnh CSDL thật
(dữ liệu thật không bị đụng), so 11 chỉ số (người dùng, sản phẩm, SKU, đơn, dòng đơn, tổng tiền đơn, đánh giá, số bút
toán, nợ − có, khung Flash Sale, tham số), sao mọi bucket MinIO ra `backups/minio-drill` rồi phục hồi thử vào bucket tạm
và đếm lại tệp, cuối cùng xoá CSDL / bucket tạm. Thoát với mã 0 chỉ khi mọi số khớp.

Phục hồi thật (thay dữ liệu đang chạy): `deploy/scripts/restore.sh <tệp .dump> [--files]` — docs/04 mục "Sao lưu & phục hồi".

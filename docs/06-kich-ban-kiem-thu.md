# 06 — Kịch bản kiểm thử

Mỗi dòng là một kịch bản **chạy tự động** (Playwright trên stack Docker, dữ liệu thật) trừ khi cột Bước ghi "tay".
Cột *Thực tế* / *Đạt* ghi lần chạy toàn bộ gần nhất; chạy lại:

```bash
docker compose up -d --build            # stack dev, có dữ liệu mẫu
cd e2e && SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=… SH_E2E_ADMIN_PASSWORD=… npx playwright test --workers=2
```

(gateway e2e nên nâng giới hạn: `SH_GATEWAY_RATE=100r/s SH_GATEWAY_BURST=200`, mọi trình duyệt cùng một IP.)

Lần chạy gần nhất: **2026-10-07, bản cài mới** (Phase 14 H — project Docker riêng, CSDL / MinIO / Meilisearch / Redis
**trắng**, dữ liệu gieo lúc nạp: 1.000 sản phẩm, 606 đơn, 800 đánh giá; xong thì gỡ đúng các volume ấy):
Playwright **77 kịch bản, 77 đạt** (lần đầu 67 đạt + 2 chập chờn + 8 hỏng: 8 do helper e2e chỉ tìm sản phẩm trên trang
đầu "bán chạy" — L122, 2 do trùng chữ với toast mới — đã sửa, chạy lại toàn bộ thì đạt hết). k6 Flash Sale: **chưa đạt**
ngưỡng thời gian (KB38, L123). Sao lưu / phục hồi: đạt (KB40). Phần phụ trợ: backend `dotnet test` 187 unit + 252 tích
hợp đạt (lượt chạy lại trước commit: 251/252, 1 chập chờn — L147); vitest web 25, seller 22, admin 13 đạt. Sandbox thật (KB41): chưa chạy — chờ tài khoản.

## A. Mục 9 của đặc tả (bắt buộc)

| Mã | Chức năng | Bước | Mong đợi | Thực tế | Đạt |
|---|---|---|---|---|---|
| KB01 | Đăng ký SĐT + OTP | Đăng ký SĐT mới → đọc OTP từ hộp SMS giả lập → đặt mật khẩu → đăng nhập → thêm địa chỉ Tỉnh/Quận/Phường (`e2e.spec.cjs`) | Tài khoản tạo được, đăng nhập được, địa chỉ hiện trong sổ | Như mong đợi | ✔ |
| KB02 | Gộp giỏ khách | Khách thêm giỏ → đăng nhập (`commerce.spec.cjs`) | Dòng của khách nằm trong giỏ tài khoản, không trùng | Như mong đợi | ✔ |
| KB03 | Tách đơn theo shop + giảm giá | Giỏ 2 shop + voucher shop + voucher sàn + xu → COD (`commerce.spec.cjs`) | 2 đơn, mỗi shop một mã; tổng hiển thị = tổng 2 đơn = PricingEngine từng đồng | Như mong đợi | ✔ |
| KB04 | Thanh toán online giả lập | `SimulatedGateway`: Thành công / Thất bại → Thanh toán lại / Bỏ đi → quá hạn (`commerce.spec.cjs`) | Thành công: "Chờ xác nhận"; thất bại: trả lại được; bỏ đi: đơn huỷ, kho nhả | Như mong đợi | ✔ |
| KB05 | Vòng đời giao hàng | Shop xác nhận → in phiếu giao (PDF hợp lệ) → hãng giả lập giao → người mua "Đã nhận" (`orders.spec.cjs`); đánh giá, giải ngân, rút tiền: bộ tích hợp Phase 7–8 | Trạng thái đi đúng máy trạng thái; PDF mở được, có mã vận đơn | Như mong đợi | ✔ |
| KB06 | Huỷ đơn | Huỷ trước xác nhận / yêu cầu huỷ sau xác nhận / shop từ chối (`orders.spec.cjs`) | Huỷ ngay nhả kho; yêu cầu chờ shop; từ chối giữ đơn | Như mong đợi | ✔ |
| KB07 | Trả hàng một phần + khiếu nại | Trả 1 phần → shop từ chối → khiếu nại → sàn phân xử (`orders.spec.cjs`) | Hoàn đúng phần đã trả của dòng ấy sau phân bổ giảm giá | Như mong đợi | ✔ |
| KB08 | Flash Sale tranh suất | 30 phiên trình duyệt song song tranh 10 suất (`flash.spec.cjs`) | Đúng 10 đơn, phiên thua thấy "hết suất" | Như mong đợi | ✔ |
| KB09 | Đăng sản phẩm → tìm thấy | Đăng ký shop → duyệt → đăng sản phẩm 2 tầng phân loại → quản trị duyệt → tìm không dấu, lọc facet (`seller.spec.cjs`, `e2e.spec.cjs`) | Sản phẩm hiện trong kết quả, số facet = số kết quả khi lọc | Như mong đợi | ✔ |
| KB10 | Chat thời gian thực | Hai phiên trình duyệt người mua ↔ shop (`chat.spec.cjs`) | Tin, thẻ sản phẩm, đang gõ, đã xem, trả lời nhanh tới ngay | Như mong đợi | ✔ |
| KB11 | IDOR | Người mua A mở đơn của B; nhân viên shop X sửa sản phẩm shop Y (`orders.spec.cjs`) | 404, không 403, không lộ dữ liệu | Như mong đợi | ✔ |
| KB12 | Khoá cắt phiên | Quản trị khoá người mua đang đăng nhập (`hardening.spec.cjs`) | Yêu cầu kế tiếp 401, bị đưa về đăng nhập, mật khẩu không còn mở phiên | Như mong đợi | ✔ |
| KB13 | Giao diện theo khổ | Mọi trang `web` ở 375 px; `seller` / `admin` ở 1366 × 768 (`hardening.spec.cjs`) | Không cuộn ngang | Lần đầu đỏ (L041–L043), sau sửa đạt | ✔ |

## B. 19 kịch bản cũ (chạy trên dữ liệu thật, `e2e.spec.cjs`)

| Mã | Chức năng | Bước | Mong đợi | Thực tế | Đạt |
|---|---|---|---|---|---|
| KB14 | Trang chủ | Mở `/` | Đủ khối: header, banner, lối tắt, danh mục, Mall, tìm kiếm hàng đầu, gợi ý; Flash Sale khi có khung đang chạy; không lỗi console | Như mong đợi | ✔ |
| KB15 | Dữ liệu thật trang chủ | So khối trang chủ với API | Cùng sản phẩm / số liệu | Như mong đợi | ✔ |
| KB16 | Xem thêm | Bấm "Xem Thêm" ở Gợi ý hôm nay | Thêm sản phẩm, không trùng | Như mong đợi | ✔ |
| KB17 | Gợi ý tìm kiếm | Gõ không dấu ở header | Gợi ý từ máy chủ có dấu | Như mong đợi | ✔ |
| KB18 | Chi tiết sản phẩm | Bấm thẻ sản phẩm | URL chuẩn `/san-pham/{slug}-i.{shop}.{id}`, đúng tên, giá | Như mong đợi | ✔ |
| KB19 | Bắt buộc chọn phân loại | Thêm giỏ khi chưa chọn; phân loại hết hàng | Báo chọn phân loại; lựa chọn hết hàng mờ, không bấm được | Như mong đợi | ✔ |
| KB20 | Đăng nhập | Đăng nhập đúng / sai mật khẩu | Header đổi tên người dùng / báo sai, không vào được | Như mong đợi | ✔ |
| KB21 | Yêu thích | Khách bấm tim / người mua bấm tim | Khách → trang đăng nhập; người mua lưu trên máy chủ | Như mong đợi | ✔ |
| KB22 | Giỏ hàng | Tick chọn dòng, đổi số lượng, xoá, giỏ trống | Tổng chỉ của dòng chọn; trạng thái trống đúng | Như mong đợi | ✔ |
| KB23 | Thanh toán COD | Thanh toán khi chưa / đã đăng nhập | Đòi đăng nhập; tạo đơn thật có mã | Như mong đợi | ✔ |
| KB24 | Tìm không dấu | Tìm "dien thoai" | Ra "Điện Thoại…" | Như mong đợi | ✔ |
| KB25 | Facet | Bấm một facet | Số trên facet = số kết quả | Như mong đợi | ✔ |
| KB26 | Lọc / sắp xếp | Khoảng giá sai; lọc sao; sắp giá có / không từ khoá | Báo lỗi rõ; lọc đúng; thứ tự giá đúng | Như mong đợi | ✔ |
| KB27 | Danh mục | Bấm danh mục | Trang danh mục có breadcrumb | Như mong đợi | ✔ |
| KB28 | Trang shop | Theo dõi shop | Số người theo dõi tăng 1 (tính lại từ bảng) | Như mong đợi | ✔ |
| KB29 | Thông báo | Mở `/thong-bao` | Đòi đăng nhập; hiện thông báo thật | Như mong đợi | ✔ |
| KB30 | Popup quảng cáo | Khách mới mở trang chủ, đóng, tải lại | Hiện một lần, không hiện lại | Như mong đợi | ✔ |
| KB31 | Footer | Mở trang | Đủ cột, thông tin pháp nhân từ tham số | Như mong đợi | ✔ |

## C. Bổ sung Phase 2–13

| Mã | Chức năng | Bước | Mong đợi | Thực tế | Đạt |
|---|---|---|---|---|---|
| KB32 | Phân quyền quản trị | Sai mật khẩu quản trị; người mua mở `/admin/` (`admin.spec.cjs`) | Từ chối | Như mong đợi | ✔ |
| KB33 | Báo cáo | Tổng quan, GMV xuất Excel, tra đơn, huỷ có lý do; shop xem phân tích (`reports.spec.cjs`) | Số liệu khớp, tệp Excel mở được | Như mong đợi | ✔ |
| KB34 | Trang pháp lý & trợ giúp | Mở điều khoản, quy chế, trung tâm trợ giúp (`reports.spec.cjs`) | Nội dung CMS đầy đủ, tìm trợ giúp không dấu | Như mong đợi | ✔ |
| KB35 | SEO | Bot mở trang sản phẩm, đường cũ, sitemap, robots (`seo.spec.cjs`) | HTML render sẵn có JSON-LD; 301; sitemap chia tệp; người thật nhận SPA | Như mong đợi | ✔ |
| KB36 | Tiếp cận (WCAG AA) | axe trên trang người mua, đăng nhập seller/admin; Tab tới ô tìm kiếm (`a11y.spec.cjs`) | Không lỗi nghiêm trọng; viền focus rõ | Lần đầu đỏ (L044), sau sửa đạt | ✔ |
| KB37 | Stack | Seller, admin mở được; gateway chuyển `/health` (`stack-smoke.spec.cjs`) | 200 | Như mong đợi | ✔ |
| KB43 | Tài khoản phụ | Chủ shop **mời** CSKH → nhân viên đăng nhập, mở *Lời mời* và đồng ý → chỉ thấy mục được cấp → gỡ (`shop-design.spec.cjs`) | Nhân viên chỉ thấy mục được cấp; gỡ thì gọi API kế tiếp 404 | Như mong đợi (theo luồng lời mời, L100) | ✔ |
| KB44 | Trang trí & danh mục shop | Lưu hồ sơ chỉ đổi logo; tạo danh mục, trang trí 3 khối, đổi thứ tự (`shop-design.spec.cjs`) | Giới thiệu không mất; người mua thấy tab Dạo theo đúng thứ tự, tab danh mục, Hồ sơ shop | Lần đầu đỏ (L047), sau sửa đạt | ✔ |
| KB45 | Ảnh CCCD khi đăng ký bán | Tải ảnh CCCD (`shop-design.spec.cjs`) | Ảnh xem trước hiện được | Lần đầu đỏ (L050), sau sửa đạt | ✔ |
| KB46 | Excel hàng loạt | Tải tệp mẫu theo ngành; tải tệp giá & tồn rồi đưa lên (`shop-design.spec.cjs`) | Tệp .xlsx; việc nền Hangfire xong, báo kết quả | Như mong đợi | ✔ |
| KB47 | Freeship Xtra | Shop bật / tắt (`shop-design.spec.cjs`) | Thấy mức phí; trạng thái lưu đúng | Lần đầu đỏ (L051), sau sửa đạt | ✔ |
| KB48 | Ảnh đại diện, quyền riêng tư | Cắt & lưu ảnh; tải dữ liệu; xoá tài khoản (`account.spec.cjs`) | Ảnh hiện ở header; tệp JSON đúng người; đăng nhập lại 401 | Như mong đợi | ✔ |
| KB49 | Giới hạn mua, sản phẩm tương tự | Giới hạn 2; thêm cái thứ 3; shop hết hàng (`cart-rules.spec.cjs`) | Ô số lượng dừng ở 2; giỏ 409; dòng hết hàng mở được sản phẩm tương tự | Như mong đợi | ✔ |
| KB50 | Chặn / báo cáo shop | Người mua chặn + báo cáo; quản trị phạt (`chat.spec.cjs`) | Shop không gửi được tin (409); 2 điểm phạt | Như mong đợi | ✔ |
| KB51 | Liên hệ shop | Đơn mua → Liên hệ shop (`orders.spec.cjs`) | Chat mở đúng shop kèm thẻ đơn | Lần đầu thiếu chức năng (L052), sau bổ sung đạt | ✔ |
| KB52 | Đa kho | Shop thêm kho Hà Nội, bật đa kho; người mua mua 2 sản phẩm ở 2 kho (`shop-design.spec.cjs`) | Trang thanh toán báo 2 kiện; một đơn, 2 vận đơn; chi tiết đơn hiện 2 kiện | Như mong đợi | ✔ |
| KB53 | Trang sản phẩm & shop | Lưu voucher shop; đổi tỉnh nhận; tìm trong shop; theo dõi; đã xem; giỏ (`storefront-extras.spec.cjs`) | Voucher "Đã lưu"; phí đổi theo tỉnh; kết quả trong shop; shop ở "Shop theo dõi"; sản phẩm ở "Đã xem"; giỏ có gợi ý | Lần đầu đỏ (L059), sau sửa đạt | ✔ |
| KB54 | Shop liên quan, thương hiệu ngành | Tìm "techzone"; trang ngành bấm thương hiệu (`storefront-extras.spec.cjs`) | Khối shop dẫn tới shop; thương hiệu lọc kết quả | Như mong đợi | ✔ |
| KB55 | Facet vận chuyển & dịch vụ | Tìm "ao", bấm *Thanh toán khi nhận hàng*, rồi một đơn vị vận chuyển (`storefront-extras.spec.cjs`) | Số kết quả đúng bằng số đếm của facet | Như mong đợi | ✔ |
| KB56 | Công cụ người bán | Bảng điều khiển; chọn sản phẩm → Ẩn; Sao chép (`shop-design.spec.cjs`) | Có lượt xem, thông báo của sàn; "Đã xử lý 1/1", trạng thái *Đã ẩn*; mở trang sửa bản sao | Như mong đợi | ✔ |
| KB57 | Xuất Excel chạy nền | Đơn hàng → Xuất Excel (`shop-design.spec.cjs`) | Việc nền chạy, trình duyệt tải tệp .xlsx | Như mong đợi | ✔ |
| KB58 | Chiến dịch của sàn | Shop chọn sản phẩm đăng ký; sàn duyệt; mở `/su-kien/…` (`shop-design.spec.cjs`) | *Chờ duyệt* → sản phẩm hiện trên trang chiến dịch | Như mong đợi | ✔ |
| KB59 | Địa chỉ tại trang thanh toán | Người mua chưa có địa chỉ → *Thêm địa chỉ mới* (`storefront-extras.spec.cjs`) | Địa chỉ mới hiện, nút đặt hàng bật | Như mong đợi | ✔ |
| KB60 | ZaloPay | Đặt đơn ZaloPay (thẻ ATM) → callback ký key2 hai lần → huỷ đơn (`ProviderTests`) | Đơn đã trả một lần (lần hai `return_code` 2), huỷ → gọi `/v2/refund`, đơn *Đã hoàn tiền* | Như mong đợi | ✔ |
| KB61 | Trả góp | Đơn ₫150.000 chọn ZaloPay → Trả góp; đơn ₫3.500.000 chọn lại (`ProviderTests`) | Đơn nhỏ: mờ, lý do "Trả góp áp dụng cho đơn từ ₫3.000.000"; VNPay / MoMo không có trả góp; đơn lớn: ZaloPay nhận mã trả góp của hợp đồng, thanh toán lại giữ hình thức | Như mong đợi | ✔ |
| KB62 | Việc nền | Admin → Việc nền → *Mở bảng Hangfire* (`reports.spec.cjs`) | Danh sách lịch chạy hiện; tab mới mở bảng Hangfire, mã dùng một lần không còn trên URL | Như mong đợi | ✔ |
| KB63 | Đăng ký bằng email | Chọn *Email* ở `/dang-ky` → mã đọc từ Mailpit → đặt mật khẩu → đăng nhập lại bằng email (`register-email.spec.cjs`) | Tài khoản tạo bằng email, đăng nhập được | Như mong đợi | ✔ |
| KB64 | Trang sản phẩm | Phóng to ảnh (Esc đóng); khối shop có điểm đánh giá; *Mua ngay* khi giỏ đã có dòng khác (`product-page.spec.cjs`) | Lightbox mở / đóng; sang thanh toán chỉ với dòng ấy, dòng kia vẫn trong giỏ | Như mong đợi | ✔ |
| KB65 | Voucher shop trong giỏ | Chọn mã của shop ngay trong khối giỏ → thanh toán (`cart-voucher.spec.cjs`) | Khối hiện "Shop giảm ₫20.000"; trang thanh toán dùng đúng mã ấy | Như mong đợi | ✔ |
| KB66 | Ghim bản đồ địa chỉ | Thêm địa chỉ, *Ghim vị trí* → bấm bản đồ → lưu (`address-map.spec.cjs`) | Toạ độ lưu ở API; toast báo đã lưu | Như mong đợi | ✔ |
| KB67 | Shop bị từ chối | Quản trị từ chối hồ sơ → chủ shop thấy lý do, không có menu bán hàng → gửi lại hồ sơ (`shop-review.spec.cjs`) | Trạng thái "Chờ duyệt" sau khi gửi lại | Như mong đợi | ✔ |
| KB68 | Hỏi trước khi xoá | Giỏ: *Xóa* → *Không* giữ dòng → *Xóa* → *Đồng ý* (`e2e.spec.cjs`) | Dòng chỉ mất sau khi đồng ý | Như mong đợi | ✔ |
| KB69 | Lối tắt & footer | Trang chủ có Mã Giảm Giá / Freeship / Deal Sốc, lưới Mall; footer chỉ hiện mạng xã hội có liên kết, *Flash Sale* → `/flash-sale` (`e2e.spec.cjs`) | Đúng theo API | Như mong đợi | ✔ |

## D. Kiểm bằng công cụ khác / tay

| Mã | Chức năng | Bước | Mong đợi | Thực tế | Đạt |
|---|---|---|---|---|---|
| KB38 | Tải Flash Sale 1.000 người | k6 `e2e/load/flash-sale.js` (docs/04) | Không lỗi máy chủ, suất bán = số đơn ≤ quota, p95 đặt hàng < 3 s | Bản cài mới: đúng 75/75 suất → 75 đơn, 925 từ chối đúng luật, 0 lỗi máy chủ; **p95 đặt hàng 4,30 s (ngưỡng 3 s)**. Cùng máy, mã trước Phase 14 (`ea48b76`): p95 3,37 s — L123 | ✘ |
| KB39 | Hiệu năng 1 triệu sản phẩm | k6 `e2e/load/perf.js` trên stack `shophub-perf` | Tìm kiếm p95 < 500 ms, trang < 300 ms | Không chạy lại ở lượt này (cần stack `shophub-perf` 1 triệu sản phẩm); số gần nhất 2026-10-07: 131 / 110 / 102 / 62 / 58 ms, 0 lỗi | — |
| KB40 | Sao lưu / phục hồi | tay: Quản trị → Việc nền → *Sao lưu ngay* (`sys.backup`, tệp `backups/db/shophub-*.dump`) → xoá dữ liệu → `restore.sh <tệp>` | Số đơn, sản phẩm, tổng sổ cái như trước | Bản cài mới: 735 đơn, 1.052 sản phẩm, tổng nợ sổ cái 2.967.019.770 ₫ → `TRUNCATE` đơn (0) → khôi phục: 735 / 1.052 / 2.967.019.770 ₫ khớp | ✔ |
| KB41 | Cổng / hãng thật | tay: VNPay, MoMo, GHN, GHTK sandbox (docs/04) | Thanh toán, IPN, vận đơn, webhook chạy với tài khoản thử | **Chưa chạy** — chưa có tài khoản sandbox; đã kiểm với bản giả lập cùng giao thức (docs/07 Phase 11) | — |
| KB42 | Đồng thời trong CSDL | `dotnet test` (tồn kho 50 yêu cầu / 10 chiếc, voucher, idempotency, webhook, theo dõi, rút tiền…) | Đúng giới hạn, không âm | Đạt | ✔ |

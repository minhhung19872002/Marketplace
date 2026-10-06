# **ShopHub** — SÀN THƯƠNG MẠI ĐIỆN TỬ ĐA NGƯỜI BÁN (mô hình Shopee)

> Prompt dựng sản phẩm cho Claude Code. Đặt tệp này vào gốc repo `Marketplace` dưới tên `CLAUDE.md`
> (hoặc giữ tên `PROMPT-BUILD-SHOPHUB.md` rồi bảo Claude Code: *"Đọc PROMPT-BUILD-SHOPHUB.md và làm
> Phase 1"*). **Đọc phần A trước khi làm bất cứ việc gì.**

---

## A. HIỆN TRẠNG REPO — ĐỌC TRƯỚC

Repo `minhhung19872002/Marketplace` hiện là **frontend thuần, dữ liệu giả**, một commit duy nhất:

| Hạng mục | Hiện có | Ghi chú |
|---|---|---|
| Stack | React 18 + Vite 5 + react-router v6, **JavaScript** (`.jsx`), CSS thuần mỗi component một tệp | Không TypeScript, không thư viện UI |
| Dữ liệu | `src/data/products.js`: 40 sản phẩm sinh tất định từ hàm `seeded()`, 18 danh mục, 9 tên shop, ảnh từ `cdn.dummyjson.com` + SVG dự phòng | Đánh giá, lượt bán, tồn kho đều là số bịa |
| Trạng thái | 3 Context lưu `localStorage`: `CartContext` (khoá dòng = `id::biến thể`), `AuthContext` (đăng nhập **không kiểm mật khẩu**), `WishlistContext` | |
| Trang (11) | `/`, `/san-pham/:id`, `/gio-hang`, `/tim-kiem`, `/dang-nhap`, `/dang-ky`, `/thanh-toan`, `/dat-hang-thanh-cong`, `/yeu-thich`, `/thong-bao`, `/shop` | Lazy-load từng trang |
| Thành phần | Header (gợi ý tìm kiếm, xem nhanh giỏ, menu người dùng), Banner carousel, CategoryGrid, CategoryShortcuts, FlashSale (đếm ngược + thanh đã bán), MallBrands, ProductCard/Grid, Footer, BackToTop | |
| Nghiệp vụ đã mô phỏng | Bắt buộc chọn phân loại trước khi thêm giỏ; tick chọn dòng giỏ mới tính tiền; tìm không dấu; lọc danh mục/giá/sao; sắp xếp liên quan/mới/bán chạy/giá; 3 cách giao (16k/30k/60k); 3 mã giảm giá cứng `SHOPHUB50`, `FREESHIP`, `SALE12`; 3 cách trả tiền (chỉ là radio) | |
| Kiểm thử | `e2e.spec.cjs`: 19 phép thử Playwright | Chạy trên dữ liệu giả |

**Những chỗ hiện đang "trông như chạy" nhưng chưa có thật** — chính là việc phải làm:

1. Không có backend, không CSDL: mọi số liệu (đã bán, đánh giá, tồn kho, lượt thích) là số bịa.
2. Đăng nhập nhận mọi mật khẩu; không có tài khoản thật, không phân vai người mua / người bán / quản trị.
3. Giỏ hàng không chia theo shop; thanh toán gộp mọi shop vào một đơn, một phí giao hàng.
4. Mã giảm giá viết cứng trong `Checkout.jsx`, không điều kiện đơn tối thiểu, không hạn dùng, không giới hạn lượt.
5. Đặt hàng chỉ xoá dòng khỏi giỏ rồi chuyển trang: không tạo đơn, không trừ kho, không có trang "Đơn mua".
6. Flash Sale đếm ngược về một mốc cố định; "đã bán" không liên quan tới đơn nào.
7. Chỉ một trang shop (`/shop`) cứng cho "ShopHub Official Store"; nút Theo dõi, Chat không làm gì.
8. Thông báo là 5 dòng cứng; không có Kênh Người Bán, không có trang quản trị sàn.

**Giữ lại:** đường dẫn tiếng Việt, bố cục và trải nghiệm hiện có (đã đúng chất Shopee), 19 phép thử
e2e (sửa cho chạy trên dữ liệu thật, không xoá). **Thay:** nguồn dữ liệu giả → API thật; Context
`localStorage` → TanStack Query + server; JavaScript → TypeScript.

> **Thương hiệu.** Bố cục giống Shopee thì được; **không** dùng tên, logo, linh vật, chữ "Shopee",
> "ShopeePay", "Shopee Mall", "Shopee Xu" ở bất cứ đâu, kể cả id trong mã (`featureShortcuts` hiện có
> `id: 'shopee-mall'`, `'shopee-4sao'` — đổi). Màu cam chủ đạo đặt thành token để đổi được.

---

## 0. VAI TRÒ VÀ NHIỆM VỤ

Bạn là kỹ sư phần mềm chính, xây dựng **ShopHub** — sàn thương mại điện tử **đa người bán**
(marketplace C2C + B2C) cho thị trường Việt Nam, đầy đủ ba phía: **Người mua**, **Người bán (Kênh
Người Bán)**, **Quản trị sàn**.

**Không được stub.** Mọi chức năng liệt kê ở đây phải chạy thật với dữ liệu thật trong CSDL. Chức
năng nào cần dịch vụ bên ngoài (cổng thanh toán, hãng vận chuyển, SMS, FCM) thì dựng **qua interface**
với hai bản cài: bản **sandbox/thật** dùng khoá cấu hình, và bản **giả lập nội bộ** chạy đúng vòng
đời (gọi lại webhook, đổi trạng thái theo thời gian) để demo được khi chưa có hợp đồng. Không bao giờ
viết hàm rỗng trả `true`. Chưa làm được thì dừng lại hỏi.

Giao diện, thông báo, dữ liệu mẫu, tài liệu đều bằng **tiếng Việt**. Tiền tệ **VND**.

### 0.1. Định danh sản phẩm

| Hạng mục | Giá trị |
|---|---|
| Tên sản phẩm | **ShopHub** |
| Slug repo | `marketplace` |
| Namespace .NET | `ShopHub.*` — solution `ShopHub.sln` |
| Gói npm | `@shophub/web` (người mua), `@shophub/seller`, `@shophub/admin` |
| Docker image | `shophub/api`, `shophub/web`, `shophub/seller`, `shophub/admin` |
| CSDL / user | `shophub` / `shophub` |
| Prefix biến môi trường | `SH_` (`SH_DB_HOST`, `SH_JWT_SECRET`, `SH_VNPAY_TMN_CODE`…) |
| Bucket MinIO | `sh-products`, `sh-reviews`, `sh-kyc` (riêng tư), `sh-chat`, `sh-banners` |
| Prefix Redis | `sh:` |
| Issuer JWT | `ShopHub` |
| Tiền tố mã đơn | `SH` + yyMMdd + 6 ký tự (`SH261006A7K2Q9`) — cấu hình được |
| Tên "xu" thưởng | **ShopHub Xu** · tên ví: **Ví ShopHub** · khu chính hãng: **ShopHub Mall** |

Tên sàn, logo, hotline, địa chỉ pháp nhân, mức phí là **tham số hệ thống**, không viết cứng.

---

## 1. STACK

| Thành phần | Công nghệ | Ghi chú |
|---|---|---|
| Backend | .NET 8 ASP.NET Core Web API | Clean Architecture + MediatR (CQRS nhẹ) + FluentValidation |
| ORM | EF Core 8 + Npgsql | Code-first, migration |
| CSDL | PostgreSQL 16 | `unaccent`, `pg_trgm`; collation `vi-VN-x-icu` |
| Tìm kiếm | **Meilisearch** (gõ sai chính tả, không dấu, facet, xếp hạng tuỳ chỉnh) | Đồng bộ qua outbox; PostgreSQL FTS là đường dự phòng khi Meili tắt |
| Cache / khoá / hàng đợi | Redis 7 | Giỏ khách vãng lai, khoá phân tán, giới hạn tốc độ, bộ đếm Flash Sale |
| Việc nền | Hangfire (PostgreSQL storage) | Tự huỷ đơn chưa trả, tự xác nhận nhận hàng, đối soát, tính phí |
| Thời gian thực | SignalR (Redis backplane) | Chat, thông báo, trạng thái đơn, tồn kho Flash Sale |
| Lưu tệp | MinIO (S3) | Ảnh sản phẩm sinh 3 cỡ (WebP), video ≤ 30 s |
| Frontend (×3) | React 18 + **TypeScript** + Vite | `web` (người mua, SSR phần SEO — xem 6.6), `seller`, `admin` |
| UI | `web`: CSS module theo thiết kế hiện có; `seller` + `admin`: Ant Design 5 | |
| State | TanStack Query + Zustand | |
| Biểu đồ | Recharts | |
| Báo cáo | ClosedXML (Excel), QuestPDF (PDF: hoá đơn bán hàng, phiếu gửi hàng, đối soát) | |
| Log | Serilog → tệp + PostgreSQL; OpenTelemetry tuỳ chọn | |
| Auth | JWT access 15 phút + refresh token xoay vòng, lưu băm | OTP qua SMS/email; đăng nhập Google tuỳ chọn |
| Triển khai | Docker Compose + Nginx | `docker compose up -d` là lên đủ, có dữ liệu mẫu |
| Kiểm thử | xUnit + FluentAssertions + Testcontainers; Vitest; Playwright | |

---

## 2. CẤU TRÚC REPO (đích đến)

```
marketplace/
├── CLAUDE.md                     # tệp này
├── docker-compose.yml / docker-compose.prod.yml / .env.example
├── docs/                         # 00-quyet-dinh-ky-thuat … 08-so-loi (mục 10)
├── backend/
│   ├── ShopHub.sln
│   ├── src/
│   │   ├── ShopHub.Domain/          # Entity, value object (Money, Sku…), enum, domain event
│   │   ├── ShopHub.Application/     # Features/<PhânHệ>/<UseCase>: Command|Query + Handler + Validator + Dto
│   │   ├── ShopHub.Infrastructure/  # EF, Redis, MinIO, Meili, cổng thanh toán, hãng vận chuyển
│   │   ├── ShopHub.Reporting/
│   │   └── ShopHub.Api/             # Controller mỏng, middleware, hub SignalR, webhook
│   └── tests/ ShopHub.UnitTests / ShopHub.IntegrationTests
├── web/            # (đổi tên từ src/ hiện có) site người mua — giữ bố cục cũ, chuyển TS
├── seller/         # Kênh Người Bán
├── admin/          # Quản trị sàn
├── e2e/            # Playwright (chuyển e2e.spec.cjs vào đây)
└── deploy/ nginx/, postgres/init/, scripts/backup.sh, restore.sh
```

Nguyên tắc: **mọi nghiệp vụ ở API**. Frontend không tự tính giá cuối, phí ship, giảm giá rồi gửi
xuống — máy chủ tính lại toàn bộ và trả về; client chỉ hiển thị.

---

## 3. NGHIỆP VỤ CỐT LÕI — PHẦN DỄ SAI NHẤT, ĐỌC KỸ

### 3.1. Tiền

- Lưu **số nguyên VND** (`bigint`), không `decimal` lẻ, không `float`. Kiểu `Money` ở Domain.
- Mọi phép chia (phân bổ giảm giá cho từng dòng, chia phí) dùng **phương pháp dư lớn nhất**: tổng các
  phần sau khi làm tròn phải bằng đúng tổng gốc. Có phép thử cho chuyện này.
- Giá hiển thị và giá chốt: lúc đặt đơn **chụp lại** (snapshot) tên, ảnh, phân loại, giá của từng
  dòng vào `order_items`. Người bán sửa giá sau đó không được làm đổi đơn cũ.
- Định dạng hiển thị `₫1.250.000` (giữ `formatPrice` hiện có, chuyển vào `lib/money.ts`); văn hoá
  mặc định của tiến trình .NET là `vi-VN`.

### 3.2. Sản phẩm – phân loại – SKU

- **Sản phẩm (SPU)** có tối đa **2 tầng phân loại** (ví dụ Màu × Size); mỗi tầng ≤ 20 lựa chọn, mỗi
  lựa chọn tầng 1 có thể có ảnh riêng.
- **SKU** = một tổ hợp lựa chọn; mỗi SKU có giá, giá gốc, tồn kho, mã SKU người bán, cân nặng, kích
  thước đóng gói. Sản phẩm không phân loại vẫn có **đúng một SKU**.
- Giỏ hàng và đơn hàng trỏ tới **SKU**, không trỏ tới sản phẩm (thay khoá `id::biến thể` hiện có).
- Thuộc tính ngành hàng (Thương hiệu, Xuất xứ, Chất liệu…) do quản trị khai theo **từng danh mục lá**,
  có bắt buộc/không, kiểu (chọn một, chọn nhiều, chữ, số + đơn vị). Không viết cứng `specs`.
- Trạng thái sản phẩm: `NHÁP → CHỜ DUYỆT → ĐANG BÁN ⇄ ẨN`, `BỊ KHOÁ` (vi phạm, kèm lý do),
  `ĐÃ XOÁ` (xoá mềm). Sửa trường "nhạy cảm" (tên, ảnh, danh mục) của sản phẩm đã duyệt → duyệt lại
  nếu tham số `PRODUCT.REVIEW_ON_EDIT` bật.

### 3.3. Tồn kho — chống bán âm

- Tồn kho có ba con số trên SKU: `stock` (thực có), `reserved` (đã giữ cho đơn chưa trả tiền /
  chưa giao), `available = stock − reserved`. **Ràng buộc CHECK** ở CSDL: `reserved ≥ 0`, `stock ≥ reserved`.
- Giữ hàng bằng **một câu UPDATE có điều kiện** (`… SET reserved = reserved + @n WHERE id = @id AND
  stock - reserved >= @n`), kiểm số dòng bị ảnh hưởng. Không "đọc rồi ghi" ở tầng nghiệp vụ.
- Vòng đời: đặt đơn → **giữ**; đơn online quá hạn trả tiền (mặc định 15 phút, tham số) → **nhả**;
  người bán giao cho hãng vận chuyển → **trừ thật** (`stock −= n`, `reserved −= n`); huỷ trước khi
  giao → nhả; trả hàng hoàn kho → cộng lại `stock`.
- Mỗi lần đổi tồn kho ghi `inventory_movements` (lý do, đơn, người làm). Phép thử song song: 50 yêu
  cầu đặt cùng một SKU còn 10 chiếc → đúng 10 đơn thành công, `available = 0`, không âm.

### 3.4. Giỏ hàng

- Khách chưa đăng nhập: giỏ lưu Redis theo `cart_token` (cookie), **gộp vào giỏ tài khoản** khi đăng nhập.
- Giỏ **nhóm theo shop** (mỗi khối một shop, có ô chọn cả shop, mã giảm của shop, phí ship riêng).
- Mỗi lần mở giỏ, máy chủ kiểm lại: hết hàng, đổi giá (hiện giá cũ gạch), sản phẩm bị ẩn/khoá, vượt
  giới hạn mua mỗi người — báo rõ từng dòng, không âm thầm xoá.
- Đổi phân loại ngay trong giỏ; "Sản phẩm tương tự" cho dòng hết hàng.

### 3.5. Đặt hàng — tách đơn theo shop

- Một lần bấm "Đặt hàng" = một **checkout** (`checkout_sessions`) sinh **N đơn**, mỗi shop một đơn
  (và mỗi kho gửi của shop một kiện nếu bật đa kho). Thanh toán online trả **một lần** cho cả checkout.
- **Idempotency:** client gửi `Idempotency-Key`; bấm hai lần hoặc mạng chập chờn gửi lại không được
  sinh hai checkout. Ràng buộc duy nhất trên khoá ấy.
- Máy chủ tính lại toàn bộ (mục 3.6) và so với tổng client đã thấy; lệch thì trả 409 kèm bảng giá mới
  để người dùng xác nhận lại.

### 3.6. Thứ tự tính tiền (chốt một lần, viết một chỗ: `PricingEngine`)

```
Tiền hàng từng dòng          = đơn giá SKU (đã áp chương trình giảm giá shop / Flash Sale) × số lượng
− Ưu đãi combo / mua kèm của shop
− Voucher của shop                     (≤ 1 mã / shop)
= Tạm tính theo shop
+ Phí vận chuyển của shop              (từ hãng vận chuyển, theo cân nặng quy đổi & tuyến)
− Voucher miễn phí vận chuyển của sàn  (≤ 1 mã / checkout, phân bổ cho các shop)
− Voucher giảm giá của sàn             (≤ 1 mã / checkout, phân bổ theo tỉ lệ tiền hàng)
− ShopHub Xu                           (≤ X% giá trị đơn, tham số)
= Tổng thanh toán (≥ 0)
```

- Phân bổ giảm giá của sàn xuống **từng dòng đơn** và lưu lại (`order_item_discounts`) — cần cho
  trả hàng một phần và cho đối soát: giảm của sàn **sàn chịu**, giảm của shop **shop chịu**.
- Voucher: loại (giảm tiền / giảm % có trần / miễn ship / hoàn xu), phạm vi (toàn sàn / shop / danh
  mục / sản phẩm / người dùng mới / người theo dõi shop), đơn tối thiểu, thời gian, tổng lượt, lượt
  mỗi người, kênh. Lượt dùng trừ bằng UPDATE có điều kiện như tồn kho; huỷ đơn **trả lại lượt** nếu
  voucher còn hạn.
- Màn chọn voucher hiển thị cả mã **không dùng được** kèm lý do ("Mua thêm ₫35.000 để dùng mã này").
- Giữ ba mã mẫu hiện có (`SHOPHUB50`, `FREESHIP`, `SALE12`) làm dữ liệu gieo, không còn viết cứng.

### 3.7. Vòng đời đơn hàng (máy trạng thái — một lớp duy nhất, không gán thẳng `Status =`)

```
CHỜ THANH TOÁN ─(trả tiền)→ CHỜ XÁC NHẬN ─(shop xác nhận)→ CHỜ LẤY HÀNG ─(hãng lấy)→ ĐANG GIAO
      │(quá hạn/huỷ)               │(huỷ)                          │(huỷ)                  │
      ▼                            ▼                               ▼                       ▼
    ĐÃ HUỶ                       ĐÃ HUỶ                    ĐÃ HUỶ (hoàn tiền)     ĐÃ GIAO ─(người mua bấm
                                                                                  "Đã nhận" / tự động sau N ngày)→ HOÀN THÀNH
                                                          GIAO THẤT BẠI → ĐANG HOÀN VỀ → ĐÃ HOÀN VỀ (nhả/hoàn kho)
ĐÃ GIAO / HOÀN THÀNH (trong hạn) → YÊU CẦU TRẢ HÀNG/HOÀN TIỀN (mục 3.8)
```

- Đơn COD bỏ qua "Chờ thanh toán". Người mua tự huỷ được khi còn "Chờ xác nhận"; sau đó là **yêu
  cầu huỷ**, shop chấp thuận/từ chối trong 24 h, quá hạn thì tự chấp thuận.
- Shop không xác nhận/không giao cho hãng trong N ngày (tham số) → **tự huỷ**, ghi điểm phạt shop.
- Mỗi lần chuyển ghi `order_status_history` (từ, đến, ai/hệ thống, lý do, thời điểm). Chuyển trạng
  thái phi pháp → 409 với câu tiếng Việt, không 500.
- Nút "Đã nhận được hàng" mở **hạn đánh giá** và kích hoạt **giải ngân** cho shop.

### 3.8. Trả hàng / hoàn tiền

- Trong N ngày (mặc định 15) sau khi giao: chọn dòng & số lượng, lý do (thiếu hàng, sai hàng, hư
  hỏng, không giống mô tả, hàng giả…), ảnh/video bằng chứng, chọn **Chỉ hoàn tiền** hoặc **Trả hàng
  & hoàn tiền**.
- Shop đồng ý / từ chối / đề nghị hoàn một phần. Từ chối → người mua **khiếu nại lên sàn**, quản trị
  phân xử (xem bằng chứng hai bên, quyết định, ghi lý do).
- Trả hàng: sinh vận đơn chiều về; shop xác nhận đã nhận → hoàn tiền. Tiền hoàn = phần đã trả của
  các dòng ấy **sau phân bổ giảm giá** (mục 3.6), về đúng nguồn: online → hoàn qua cổng; COD / ví →
  vào Ví ShopHub; xu → trả xu.
- Đơn đang có yêu cầu trả hàng thì **khoá giải ngân** phần liên quan.

### 3.9. Dòng tiền — ký quỹ và đối soát

- Tiền người mua trả **không vào tay shop ngay**. Đơn HOÀN THÀNH (và hết hạn trả hàng nếu cấu hình)
  → tạo **bút toán** vào số dư shop:
  `Doanh thu = tiền hàng − giảm giá shop chịu − phí cố định (% theo ngành) − phí thanh toán (%) −
  phí dịch vụ (Freeship Xtra/Voucher Xtra nếu shop tham gia) − phí vận chuyển shop chịu`.
- Sổ cái kép (`ledger_entries`: tài khoản, nợ/có, số tiền, tham chiếu) — **số dư là tổng bút toán**,
  không phải một cột cộng dần. Có cột số dư chép sẵn thì phải tính lại từ sổ cái, có phép đo đối chiếu.
- Shop rút tiền về tài khoản ngân hàng đã xác minh: tối thiểu, số lần/tuần, sàn duyệt hoặc tự động.
- Báo cáo đối soát theo kỳ cho từng shop (Excel + PDF), khớp từng đồng với tổng các đơn.

### 3.10. Flash Sale và chương trình giảm giá

- **Flash Sale của sàn**: quản trị mở khung giờ (00:00, 09:00, 12:00, 15:00, 21:00 — cấu hình), đặt
  tiêu chí (giảm ≥ %, đánh giá ≥ sao, ngành); shop **đăng ký** SKU + giá + số suất; quản trị duyệt.
- **Flash Sale của shop** và **Chương trình giảm giá** (giảm giá theo SKU trong khoảng thời gian),
  **Combo** (mua 3 giảm 10%), **Mua kèm deal sốc**, **Quà tặng kèm**. Một SKU chỉ ở **một** chương
  trình giá tại một thời điểm — ràng buộc ở CSDL (exclusion constraint trên khoảng thời gian).
- Suất Flash Sale trừ trên Redis (Lua script nguyên tử) **và** đối chiếu xuống PostgreSQL; giới hạn
  mua mỗi người. Thanh "Đã bán N" lấy từ số suất đã trừ thật — thay số `flashSold` bịa hiện có.
- Đồng hồ đếm ngược tính theo **giờ máy chủ** (API trả `serverTime`), không theo đồng hồ máy khách.

### 3.11. Đánh giá

- Chỉ người mua có đơn **HOÀN THÀNH** chứa SKU ấy mới đánh giá được, mỗi dòng đơn một lần, trong
  N ngày; sửa được một lần trong 30 ngày.
- 1–5 sao + chữ + tối đa 6 ảnh + 1 video; thẻ chất lượng ("Đúng mô tả", "Giao nhanh"). Đánh giá đủ
  chữ + ảnh → thưởng xu (tham số).
- Người bán trả lời một lần. Lọc: có ảnh, có bình luận, theo sao, theo phân loại. Báo cáo đánh giá
  vi phạm → quản trị ẩn.
- Điểm trung bình, số đánh giá, lượt bán của sản phẩm và shop **tính lại từ dữ liệu gốc** bằng việc
  nền / sau mỗi thay đổi — không cộng dần, không gán.
- Tên người đánh giá hiện kiểu che `ng****an` (giữ cách hiện có); tuỳ chọn "ẩn danh".

### 3.12. Thời gian

Lưu UTC (`timestamptz`), hiển thị `Asia/Ho_Chi_Minh` qua **một** hàm dùng chung mỗi gói frontend
(`lib/datetime.ts`). Mọi mốc "hết hạn", "khung giờ Flash Sale", "ngày" của báo cáo tính theo giờ Việt Nam.

---

## 4. MÔ HÌNH DỮ LIỆU (PostgreSQL, `snake_case`, số nhiều)

Mọi bảng: `id uuid default gen_random_uuid()`, `created_at`, `created_by`, `updated_at`,
`updated_by`, `deleted_at` (xoá mềm), `xmin` làm khoá lạc quan cho bảng hay sửa đồng thời.

### 4.1. `iam` — tài khoản & phân quyền
```
users                -- phone (duy nhất), email (duy nhất), username, password_hash, full_name, avatar_url,
                     -- gender, date_of_birth, status(HOẠT ĐỘNG|KHOÁ|ĐÃ XOÁ), phone_verified_at,
                     -- email_verified_at, failed_login_count, locked_until, last_login_at
user_identities      -- user_id, provider(GOOGLE|FACEBOOK|APPLE), provider_key
refresh_tokens       -- user_id, token_hash, device, ip, expires_at, revoked_at, replaced_by
otp_codes            -- target, purpose(ĐĂNG KÝ|ĐĂNG NHẬP|QUÊN MK|ĐỔI SĐT), code_hash, expires_at, attempts
roles / permissions / role_permissions / user_roles   -- vai trò quản trị sàn (RBAC)
shop_staff           -- shop_id, user_id, role(CHỦ SHOP|QUẢN LÝ|CSKH|KHO), permissions(jsonb)
addresses            -- user_id, receiver_name, phone, province_code, district_code, ward_code,
                     -- street, lat, lng, type(NHÀ RIÊNG|VĂN PHÒNG), is_default
admin_divisions      -- code, name, level(TỈNH|QUẬN|PHƯỜNG), parent_code   (danh mục hành chính VN)
audit_logs           -- user_id, ip, user_agent, action, entity, entity_id, old_value, new_value, occurred_at
```

### 4.2. `catalog` — danh mục & sản phẩm
```
categories           -- parent_id, name, slug, icon_url, level, sort_order, is_active, commission_rate_bp
category_attributes  -- category_id, name, input_type, unit, is_required, is_filterable, options(jsonb)
brands               -- name, slug, logo_url, is_verified
products             -- shop_id, category_id, brand_id, name, slug, description(html đã lọc), status,
                     -- condition(MỚI|ĐÃ SỬ DỤNG), weight_g, length_mm, width_mm, height_mm,
                     -- is_preorder, preorder_days, min_price, max_price (chép sẵn, tính lại),
                     -- sold_count, rating_avg, rating_count, like_count, view_count (chép sẵn, tính lại),
                     -- ban_reason, published_at
product_attributes   -- product_id, attribute_id, value(jsonb)
product_media        -- product_id, type(ẢNH|VIDEO), url, sort_order, variant_option_id
variant_tiers        -- product_id, tier_index(0|1), name
variant_options      -- tier_id, value, image_url, sort_order
skus                 -- product_id, option_1_id, option_2_id, seller_sku, price, original_price,
                     -- stock, reserved, weight_g, is_active        CHECK(stock>=reserved AND reserved>=0)
inventory_movements  -- sku_id, delta_stock, delta_reserved, reason, ref_type, ref_id
product_reports      -- product_id, reporter_id, reason, status
```

### 4.3. `shop`
```
shops                -- owner_id, name, slug, logo_url, cover_url, description, type(CÁ NHÂN|DOANH NGHIỆP|MALL),
                     -- status(CHỜ DUYỆT|HOẠT ĐỘNG|TẠM NGHỈ|KHOÁ), vacation_until, is_preferred,
                     -- rating_avg, response_rate, response_time_s, follower_count, product_count (tính lại),
                     -- penalty_points
shop_kyc             -- shop_id, legal_name, tax_code, business_license_url, id_card_front_url,
                     -- id_card_back_url, status, reviewed_by, reviewed_at, reject_reason   (bucket riêng tư)
shop_bank_accounts   -- shop_id, bank_code, account_no(mã hoá), account_name, verified_at, is_default
shop_warehouses      -- shop_id, address fields, is_pickup_default, is_return_default
shop_decorations     -- shop_id, layout(jsonb: banner, mục nổi bật, danh mục shop)
shop_categories      -- shop_id, name, sort_order    + shop_category_products
shop_followers       -- shop_id, user_id     (duy nhất)
shop_penalties       -- shop_id, reason, points, order_id, expires_at
shop_shipping_channels -- shop_id, carrier_code, is_enabled, cod_enabled
```

### 4.4. `sales` — giỏ, đơn, thanh toán
```
carts / cart_items           -- user_id | guest_token; sku_id, quantity, is_selected   (duy nhất user+sku)
checkout_sessions            -- user_id, idempotency_key (duy nhất), address_snapshot(jsonb),
                             -- payment_method, subtotal, shipping_fee, discount_total, coin_used,
                             -- grand_total, status, payment_expires_at
orders                       -- code (duy nhất), checkout_id, buyer_id, shop_id, status, payment_status,
                             -- subtotal, shop_discount, platform_discount, shipping_fee, shipping_discount,
                             -- coin_used, grand_total, buyer_note, cancel_reason, cancelled_by,
                             -- confirmed_at, shipped_at, delivered_at, completed_at, auto_complete_at
order_items                  -- order_id, sku_id, product_id, name_snapshot, variant_snapshot, image_snapshot,
                             -- unit_price, original_price, quantity, line_total
order_item_discounts         -- order_item_id, source(SHOP|PLATFORM|COMBO|FLASH|COIN), ref_id, amount
order_status_history         -- order_id, from_status, to_status, actor_type, actor_id, reason
payments                     -- checkout_id, method(COD|VNPAY|MOMO|ZALOPAY|THẺ|VÍ), provider_txn_id (duy nhất),
                             -- amount, status(KHỞI TẠO|THÀNH CÔNG|THẤT BẠI|HẾT HẠN|HOÀN), raw(jsonb), paid_at
payment_webhook_events       -- provider, event_id (duy nhất), payload, processed_at, result
refunds                      -- payment_id, return_id, amount, destination, status, provider_ref
return_requests / return_items / return_evidences   -- mục 3.8
disputes                     -- return_id, opened_by, admin_id, decision, decision_reason, closed_at
```

### 4.5. `logistics`
```
carriers             -- code(GHN|GHTK|VTP|SPX_SIM…), name, is_active, config(jsonb, khoá mã hoá)
shipping_rates       -- carrier_id, zone_from, zone_to, weight_from_g, weight_to_g, fee   (bảng giá giả lập)
shipments            -- order_id, carrier_id, tracking_no (duy nhất), direction(ĐI|VỀ), status,
                     -- fee, cod_amount, weight_g, label_url, expected_delivery_at
shipment_events      -- shipment_id, status, location, description, occurred_at, raw
```

### 4.6. `promo`
```
vouchers             -- code, owner_type(SÀN|SHOP), shop_id, type, discount_value, discount_percent,
                     -- max_discount, min_order, scope(jsonb), start_at, end_at, total_quota, used_count,
                     -- per_user_limit, is_public, channel
voucher_claims       -- voucher_id, user_id (lưu mã vào ví voucher)      (duy nhất)
voucher_usages       -- voucher_id, user_id, checkout_id, order_id, amount, reverted_at
promotions           -- shop_id, type(GIẢM GIÁ|COMBO|MUA KÈM|QUÀ TẶNG), name, start_at, end_at, rules(jsonb)
promotion_skus       -- promotion_id, sku_id, promo_price, quota, per_user_limit
                     -- EXCLUDE USING gist (sku_id WITH =, tstzrange(start_at,end_at) WITH &&)
flash_sale_slots     -- start_at, end_at, criteria(jsonb), status
flash_sale_items     -- slot_id, sku_id, shop_id, flash_price, quota, sold, per_user_limit, status(CHỜ|DUYỆT|TỪ CHỐI)
campaigns            -- name, slug, landing_layout(jsonb), start_at, end_at   (ngày sale 9.9, 10.10…)
banners              -- position(TRANG CHỦ|DANH MỤC|POPUP), image_url, link, start_at, end_at, sort_order
coin_ledger          -- user_id, delta, reason, ref_type, ref_id, expires_at   (số dư xu = tổng)
```

### 4.7. `finance`
```
ledger_accounts      -- owner_type(SHOP|SÀN|NGƯỜI MUA), owner_id, type(CHỜ GIẢI NGÂN|KHẢ DỤNG|VÍ)
ledger_entries       -- account_id, direction(NỢ|CÓ), amount, ref_type, ref_id, description, posted_at
fee_rules            -- category_id, fee_type(CỐ ĐỊNH|THANH TOÁN|DỊCH VỤ), rate_bp, valid_from, valid_to
settlements          -- shop_id, period_from, period_to, gross, fees, net, status, file_url
withdrawals          -- shop_id, bank_account_id, amount, status(CHỜ|ĐANG XỬ LÝ|XONG|TỪ CHỐI), processed_at
```

### 4.8. `engage` — tương tác
```
reviews / review_media / review_replies / review_reports
wishlists            -- user_id, product_id  (duy nhất)
product_views        -- user_id|session, product_id, viewed_at        (cho "Đã xem gần đây", gợi ý)
search_logs          -- keyword, user_id, result_count, occurred_at   (từ khoá hot, gợi ý)
conversations        -- buyer_id, shop_id (duy nhất cặp), last_message_at, buyer_unread, shop_unread
messages             -- conversation_id, sender_id, type(CHỮ|ẢNH|SẢN PHẨM|ĐƠN HÀNG|VOUCHER), body, payload, read_at
quick_replies        -- shop_id, shortcut, content
notifications        -- user_id, type(ĐƠN HÀNG|KHUYẾN MÃI|VÍ|HỆ THỐNG), title, body, link, is_read
notification_prefs   -- user_id, type, channel(TRONG APP|EMAIL|SMS|PUSH), enabled
device_tokens        -- user_id, platform, token
```

### 4.9. `sys`
```
system_parameters    -- key, value, data_type, group, name, description   (mọi khoá phải có chỗ đọc — mục 8)
cms_pages            -- slug, title, content   (Điều khoản, Chính sách bảo mật, Quy chế hoạt động, Trả hàng…)
outbox_messages      -- type, payload, occurred_at, processed_at, attempts   (đồng bộ Meili, gửi thông báo)
```

### 4.10. Chỉ mục & ràng buộc bắt buộc
```sql
CREATE EXTENSION IF NOT EXISTS unaccent; CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gist;
-- mọi chỉ mục duy nhất trên bảng có xoá mềm đều kèm WHERE deleted_at IS NULL
CREATE UNIQUE INDEX ux_orders_code ON sales.orders(code);
CREATE UNIQUE INDEX ux_checkout_idem ON sales.checkout_sessions(user_id, idempotency_key);
CREATE UNIQUE INDEX ux_payment_txn ON sales.payments(method, provider_txn_id) WHERE provider_txn_id IS NOT NULL;
CREATE UNIQUE INDEX ux_webhook_event ON sales.payment_webhook_events(provider, event_id);
CREATE UNIQUE INDEX ux_review_item ON engage.reviews(order_item_id) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX ux_voucher_code ON promo.vouchers(upper(code)) WHERE deleted_at IS NULL;
CREATE INDEX ix_orders_buyer ON sales.orders(buyer_id, created_at DESC);
CREATE INDEX ix_orders_shop_status ON sales.orders(shop_id, status, created_at DESC);
CREATE INDEX ix_products_name_trgm ON catalog.products USING gin (immutable_unaccent(lower(name)) gin_trgm_ops);
```

---

## 5. ĐẶC TẢ CHỨC NĂNG

> Mỗi chức năng: màn hình + API + quy tắc. Đường dẫn trang người mua **giữ tiếng Việt** như hiện có.

### PHÂN HỆ I — TÀI KHOẢN NGƯỜI DÙNG

**I.1. Đăng ký / đăng nhập**
- Đăng ký bằng số điện thoại + OTP (6 số, hạn 5 phút, tối đa 5 lần nhập sai, gửi lại sau 60 giây);
  hoặc email + xác minh qua thư. Đặt mật khẩu: ≥ 8 ký tự, có chữ và số.
- Đăng nhập: SĐT/email/tên đăng nhập + mật khẩu; **đăng nhập bằng OTP**; Google (tuỳ chọn, cấu hình).
- Khoá tạm sau N lần sai (tham số), giới hạn tốc độ theo IP và theo tài khoản.
- Quên mật khẩu qua OTP. Đổi mật khẩu → thu hồi mọi refresh token khác.
- Quản lý thiết bị đăng nhập: xem, đăng xuất từ xa.
- API: `/api/auth/register`, `/otp/send`, `/otp/verify`, `/login`, `/login-otp`, `/refresh`, `/logout`,
  `/forgot-password`, `/reset-password`.

**I.2. Hồ sơ cá nhân** (`/tai-khoan/ho-so`)
- Tên, ảnh đại diện (cắt ảnh, ≤ 1 MB), giới tính, ngày sinh; đổi SĐT/email phải qua OTP.
- **Sổ địa chỉ** (`/tai-khoan/dia-chi`): chọn Tỉnh → Quận → Phường từ danh mục hành chính, đặt mặc
  định, loại địa chỉ, ghim bản đồ tuỳ chọn. Tối đa 10 địa chỉ.
- **Tài khoản ngân hàng / thẻ** (để nhận hoàn tiền về ví rút ra): lưu mã hoá, chỉ hiện 4 số cuối.
- **Cài đặt thông báo** theo loại × kênh. **Quyền riêng tư**: tải dữ liệu của tôi, **yêu cầu xoá tài khoản**
  (chặn khi còn đơn chưa hoàn tất / còn số dư; xoá thì ẩn danh hoá dữ liệu cá nhân, giữ đơn cho kế toán).

### PHÂN HỆ II — MUA SẮM (site người mua `web/`)

**II.1. Trang chủ** (`/`) — giữ bố cục hiện có, đổi sang dữ liệu thật:
- Header: thanh trên (Kênh Người Bán → `seller/`, Trở thành Người bán, Tải ứng dụng, Thông báo có số
  chưa đọc, Hỗ trợ, tài khoản), logo, ô tìm kiếm + **gợi ý từ khoá & sản phẩm & shop**, từ khoá hot
  (lấy từ `search_logs` 7 ngày), giỏ hàng (số dòng + xem nhanh 5 dòng mới nhất).
- Banner chính + 2 banner phụ (quản trị đặt lịch), dải lối tắt (Mã giảm giá, Hàng giá hời, Mall,
  Freeship, Deal sốc… — trỏ tới trang thật), lưới danh mục 2 hàng cuộn ngang.
- **Flash Sale** khung giờ hiện tại, đếm ngược theo giờ máy chủ, thanh "Đã bán"/"Sắp cháy hàng" thật.
- **ShopHub Mall**: thương hiệu chính hãng + sản phẩm Mall.
- **Tìm kiếm hàng đầu** (top sản phẩm theo ngành), **Gợi ý hôm nay** (cuộn vô hạn / "Xem thêm", dựa
  trên lượt xem + lượt mua + ngành người dùng quan tâm; khách lạ thì theo bán chạy), mục "Đã xem gần đây".
- Popup quảng cáo (tần suất cấu hình, không làm phiền mỗi lần tải trang).

**II.2. Danh mục** (`/danh-muc/:slug`) — cây danh mục 3 cấp, banner ngành, thương hiệu nổi bật,
lưới sản phẩm với bộ lọc như trang tìm kiếm.

**II.3. Tìm kiếm** (`/tim-kiem?q=`) — giữ giao diện hiện có, thay lọc trên mảng bằng API:
- Không dấu, gõ sai 1–2 ký tự vẫn ra, đồng nghĩa cấu hình được ("đt" = "điện thoại"), khớp từng từ
  không khớp cả cụm.
- Bộ lọc facet có **số đếm thật**: danh mục, nơi bán (tỉnh), đơn vị vận chuyển, thương hiệu, khoảng
  giá, đánh giá từ N sao, loại shop (Mall / Yêu thích+), dịch vụ (Freeship, Có voucher, Hàng có sẵn,
  COD), tình trạng, thuộc tính lọc được của ngành.
- Sắp xếp: Liên quan, Mới nhất, Bán chạy, Giá ↑/↓. Phân trang máy chủ (60/trang), thứ tự ổn định.
- Khối "Shop liên quan đến từ khoá" ở đầu kết quả. Tìm **trong một shop**.
- Tìm bằng hình ảnh: **ngoài phạm vi** (ghi vào mục 12).

**II.4. Chi tiết sản phẩm** (`/san-pham/:slug-i.:shopId.:productId` — giữ `/san-pham/:id` chuyển hướng)
- Thư viện ảnh/video, phóng to, ảnh theo phân loại (bấm "Đỏ" → ảnh đổi sang ảnh màu đỏ).
- Tên + nhãn Mall/Yêu thích, sao, số đánh giá, đã bán; khung giá (giá theo SKU đang chọn, khoảng giá
  khi chưa chọn), nhãn Flash Sale + đếm ngược nếu đang trong khung.
- Voucher của shop (bấm **Lưu**), combo/mua kèm, bảo hiểm (ngoài phạm vi).
- Vận chuyển: **phí ước tính tới địa chỉ mặc định**, thời gian dự kiến, đổi địa chỉ xem lại phí.
- Chọn phân loại 2 tầng: lựa chọn **hết hàng mờ đi và không bấm được**, tồn kho theo SKU. Số lượng
  không vượt tồn và không vượt giới hạn mua. Giữ luật "bắt buộc chọn phân loại" hiện có.
- Thêm vào giỏ / Mua ngay / Yêu thích (số lượt thích thật) / Chia sẻ (sao chép liên kết, Facebook, Zalo).
- Khối shop: tên, online lần cuối, **Chat ngay**, **Xem shop**, đánh giá, tỉ lệ & thời gian phản hồi
  chat, năm tham gia, số sản phẩm, người theo dõi — **đều là số thật**.
- Chi tiết (thuộc tính ngành + breadcrumb danh mục), mô tả (HTML đã lọc XSS), đánh giá (mục 3.11, lọc,
  phân trang), sản phẩm khác của shop, sản phẩm tương tự.
- Báo cáo sản phẩm vi phạm. Ghi lượt xem (chống đếm trùng trong 30 phút).

**II.5. Trang shop** (`/shop/:slug`) — thay trang cứng hiện có:
- Ảnh bìa, logo, chỉ số thật, Theo dõi/Bỏ theo dõi, Chat. Tab: Dạo (bố cục trang trí của shop),
  Tất cả sản phẩm, danh mục của shop, Hồ sơ shop. Voucher shop, chương trình đang chạy.
- Shop đang **tạm nghỉ**: hiện băng thông báo, chặn đặt hàng.

**II.6. Giỏ hàng** (`/gio-hang`) — mục 3.4: nhóm theo shop, ô chọn shop/tất cả, đổi phân loại tại
chỗ, voucher shop theo khối, cảnh báo đổi giá/hết hàng, "Bạn có thể thích", xoá nhiều, tổng chỉ của
dòng đã chọn (giữ luật hiện có), "Mua hàng" → thanh toán.

**II.7. Thanh toán** (`/thanh-toan`) — thay form 3 ô hiện có:
- Địa chỉ nhận lấy từ sổ địa chỉ (đổi/thêm ngay tại trang).
- Mỗi shop một khối: các dòng, **lời nhắn cho shop**, **đơn vị vận chuyển** (lựa chọn & phí thật từ
  mục V, thời gian dự kiến), voucher shop.
- Voucher sàn (mục 3.6: chọn từ ví voucher hoặc nhập mã, hiện cả mã không dùng được kèm lý do), dùng
  Xu (công tắc + số xu trừ), phương thức thanh toán (mục IV; COD bị ẩn nếu vượt ngưỡng hoặc shop tắt).
- Bảng tổng: tiền hàng, phí ship, giảm phí ship, voucher, xu, **tổng thanh toán** — đều do API trả.
- Đặt hàng (Idempotency-Key) → online: chuyển cổng / hiện QR, trang chờ có đếm ngược hết hạn;
  COD: thẳng trang thành công.
- `/dat-hang-thanh-cong`: mã đơn thật (mỗi shop một mã), nút "Xem đơn hàng".

**II.8. Đơn mua** (`/tai-khoan/don-mua`)
- Tab: Tất cả, Chờ thanh toán, Vận chuyển, Chờ giao hàng, Hoàn thành, Đã huỷ, Trả hàng/Hoàn tiền;
  tìm theo mã đơn / tên shop / tên sản phẩm.
- Chi tiết đơn: dòng thời gian trạng thái, **hành trình vận đơn** (sự kiện từ hãng), địa chỉ, bảng
  tiền chi tiết, phương thức thanh toán.
- Hành động theo trạng thái: Thanh toán lại, Huỷ đơn / Yêu cầu huỷ (chọn lý do), Đã nhận được hàng,
  Trả hàng/Hoàn tiền, Đánh giá, **Mua lại** (đưa cả đơn vào giỏ), Liên hệ shop.

**II.9. Yêu thích, Đã xem, Theo dõi** — `/yeu-thich` (giữ), `/tai-khoan/da-xem`,
`/tai-khoan/shop-theo-doi`. **Ví voucher** `/tai-khoan/voucher` (còn hạn, sắp hết hạn, đã dùng,
hết hạn). **ShopHub Xu** `/tai-khoan/xu` (số dư, lịch sử, xu sắp hết hạn).

**II.10. Thông báo** (`/thong-bao`) — giữ 4 tab hiện có, đổi sang dữ liệu thật, đẩy thời gian thực qua
SignalR, đánh dấu đã đọc từng cái / tất cả, mỗi thông báo bấm vào tới đúng đơn / voucher / sản phẩm.

**II.11. Chat với người bán** — cửa sổ chat nổi góc phải (như Shopee) + trang đầy đủ:
- Gửi chữ, ảnh, **thẻ sản phẩm**, **thẻ đơn hàng**; trạng thái đã gửi / đã xem; đang gõ.
- Danh sách hội thoại, số chưa đọc, tìm hội thoại, chặn/báo cáo shop.
- Lọc thông tin liên hệ ngoài sàn (SĐT, link) theo luật cấu hình — cảnh báo, không chặn cứng.

**II.12. Trang tĩnh & pháp lý** (CMS): Điều khoản sử dụng, **Quy chế hoạt động sàn**, Chính sách bảo
mật & xử lý dữ liệu cá nhân, Chính sách trả hàng, Hướng dẫn mua hàng, Trung tâm trợ giúp (câu hỏi
thường gặp theo chủ đề, tìm kiếm). Footer hiện thông tin pháp nhân từ tham số.

**II.13. Ngày hội mua sắm** (`/su-kien/:slug`) — trang chiến dịch do quản trị dựng (banner, khối
voucher, khối Flash Sale, lưới sản phẩm theo tiêu chí).

### PHÂN HỆ III — KÊNH NGƯỜI BÁN (`seller/`)

**III.1. Đăng ký bán hàng** — từ tài khoản người mua: tên shop (duy nhất), địa chỉ lấy hàng, chọn
đơn vị vận chuyển, loại shop; cá nhân: CCCD hai mặt; doanh nghiệp: giấy phép + mã số thuế; tài khoản
ngân hàng. Trạng thái "Chờ duyệt" → quản trị duyệt/từ chối kèm lý do → thông báo.

**III.2. Bảng điều khiển** — việc cần làm (chờ xác nhận, chờ lấy hàng, đã xử lý, đơn huỷ chờ duyệt,
trả hàng chờ xử lý, sản phẩm bị khoá, sắp hết hàng), doanh số hôm nay/7/30 ngày, lượt truy cập, tỉ lệ
chuyển đổi, điểm phạt, thông báo của sàn.

**III.3. Quản lý sản phẩm**
- Danh sách: tab Tất cả / Đang hoạt động / Hết hàng / Chờ duyệt / Vi phạm / Đã ẩn; tìm, lọc theo
  danh mục, tồn kho, giá; sửa nhanh giá & tồn ngay trên bảng; ẩn/hiện, xoá, sao chép hàng loạt.
- Thêm/sửa: chọn danh mục lá (có gợi ý theo tên), ảnh (≥ 1, ≤ 9, ảnh bìa 1:1), video, tên (≤ 120 ký
  tự), mô tả, thuộc tính ngành (bắt buộc theo khai báo), **trình dựng phân loại 2 tầng** → bảng SKU
  (đặt giá/tồn/mã SKU cho tất cả hoặc từng dòng), cân nặng & kích thước, đơn vị vận chuyển cho sản
  phẩm, hàng đặt trước (số ngày chuẩn bị), tình trạng. Lưu nháp. Kiểm tra trước khi gửi: liệt kê lỗi.
- **Đăng hàng loạt bằng Excel** (tệp mẫu theo ngành, kiểm từng dòng, bảng lỗi, chạy nền) và **cập nhật
  giá/tồn hàng loạt** bằng Excel.
- Lịch sử tồn kho theo SKU (`inventory_movements`). Cảnh báo sắp hết hàng (ngưỡng theo shop).

**III.4. Quản lý đơn hàng**
- Tab theo trạng thái, lọc theo ngày, đơn vị vận chuyển, phương thức thanh toán; tìm theo mã đơn,
  tên người mua, mã vận đơn.
- **Chuẩn bị hàng**: xác nhận đơn → chọn lấy hàng tận nơi (chọn khung giờ) hoặc tự mang ra bưu cục →
  sinh vận đơn → **in phiếu giao hàng** (PDF A6/A5, mã vạch vận đơn, mã đơn, danh sách hàng) — đơn lẻ
  hoặc **hàng loạt**. In **phiếu soạn hàng** gộp nhiều đơn.
- Xử lý yêu cầu huỷ, ghi chú nội bộ, xuất danh sách đơn ra Excel.
- **Trả hàng / Hoàn tiền**: xem bằng chứng, đồng ý / từ chối (kèm bằng chứng) / đề nghị hoàn một
  phần, xác nhận đã nhận hàng trả.

**III.5. Kênh Marketing**
- **Mã giảm giá của shop** (mục 3.6): toàn shop / theo sản phẩm / cho người theo dõi / riêng tư (chỉ
  ai có mã), xem lượt lưu & lượt dùng & doanh số mang lại.
- **Chương trình giảm giá** theo SKU, **Combo khuyến mãi**, **Mua kèm deal sốc**, **Quà tặng**,
  **Flash Sale của shop** (chọn khung giờ, SKU, giá, suất).
- **Đăng ký Flash Sale / chiến dịch của sàn**: xem khung đang mở, tiêu chí, đăng ký SKU, trạng thái duyệt.
- Cảnh báo xung đột: một SKU không thể nằm ở hai chương trình giá trùng thời gian (mục 3.10).

**III.6. Chăm sóc khách hàng** — hộp chat người bán (nhiều nhân viên cùng trực, giao hội thoại,
tin trả lời nhanh, tin tự động khi ngoài giờ), quản lý đánh giá (lọc, trả lời).

**III.7. Tài chính**
- Doanh thu: **chờ giải ngân** / **đã giải ngân**, chi tiết từng đơn (tiền hàng, từng loại phí, giảm
  giá shop chịu, thực nhận) — mục 3.9.
- Số dư, **rút tiền**, lịch sử rút; tài khoản ngân hàng (thêm phải xác thực OTP).
- Báo cáo đối soát theo kỳ (Excel + PDF), hoá đơn phí sàn.

**III.8. Dữ liệu & phân tích** — doanh số, đơn, người mua, lượt xem, tỉ lệ chuyển đổi theo ngày/tuần/
tháng; top sản phẩm; nguồn truy cập; so với kỳ trước; xuất Excel. **Hiệu quả hoạt động**: tỉ lệ đơn
không thành công, giao hàng trễ, phản hồi chat, điểm phạt và hậu quả.

**III.9. Thiết lập shop** — hồ sơ shop, **trang trí shop** (kéo thả khối: banner, sản phẩm nổi bật,
danh mục, video), danh mục của shop, kho hàng & địa chỉ trả hàng, đơn vị vận chuyển, chế độ tạm nghỉ,
**tài khoản phụ** (mời nhân viên, vai trò & quyền theo mục `shop_staff`).

### PHÂN HỆ IV — THANH TOÁN

- Interface `IPaymentGateway` (`CreatePayment`, `VerifyCallback`, `Query`, `Refund`). Cài đặt:
  **VNPay sandbox**, **MoMo sandbox**, (ZaloPay tuỳ chọn) và **`SimulatedGateway`** — trang thanh toán
  giả của chính hệ thống với nút "Thành công" / "Thất bại" / "Bỏ đi", gọi webhook như cổng thật.
- **COD**: ngưỡng tối đa (tham số), tắt theo shop/đơn vị vận chuyển; thu hộ đối soát với hãng.
- **Ví ShopHub**: nạp (qua cổng), trả đơn, nhận hoàn tiền, rút về ngân hàng; mật khẩu thanh toán 6 số.
  Số dư = tổng bút toán (mục 3.9).
- Webhook/IPN: kiểm chữ ký, **xử lý đúng một lần** (ràng buộc duy nhất trên `event_id`), trả mã đúng
  khuôn của từng cổng; trang "quay về" chỉ để hiển thị, **không** dùng làm căn cứ đã trả tiền.
- Việc nền đối chiếu giao dịch treo (khởi tạo > 15 phút): hỏi lại cổng, cập nhật, nhả kho.
- Trả góp, thẻ quốc tế, "mua trước trả sau": hiển thị nếu cổng hỗ trợ, không tự dựng tín dụng.

### PHÂN HỆ V — VẬN CHUYỂN

- Interface `ICarrier` (`QuoteFee`, `CreateShipment`, `Cancel`, `GetLabel`, `Track`, webhook trạng thái).
  Cài đặt: **GHN sandbox**, **GHTK sandbox** (tuỳ khoá) và **`SimulatedCarrier`** — bảng giá theo vùng
  × cân nặng (`shipping_rates`), sinh mã vận đơn, việc nền đẩy trạng thái theo thời gian thực tế rút
  gọn (đã lấy → đang trung chuyển → đang giao → đã giao / giao thất bại) để demo trọn vòng đời.
- Cân nặng tính phí = max(cân thực, D×R×C/6000). Gộp nhiều dòng một shop thành một kiện.
- Ba mức như hiện có (Nhanh / Tiết kiệm / Hoả tốc) trở thành **kênh vận chuyển** cấu hình được, không
  còn phí cứng 16k/30k/60k.
- Trang **tra cứu vận đơn** công khai theo mã. Ngày giao dự kiến bỏ qua Chủ nhật/lễ (cấu hình).

### PHÂN HỆ VI — QUẢN TRỊ SÀN (`admin/`)

**VI.1. Tổng quan** — GMV, số đơn, người mua mới, shop mới, tỉ lệ huỷ/hoàn, doanh thu phí; theo
ngày/tuần/tháng; biểu đồ; việc chờ xử lý (shop chờ duyệt, sản phẩm chờ duyệt, khiếu nại, rút tiền).

**VI.2. Người dùng** — tìm, xem (đơn, đánh giá, vi phạm, thiết bị), khoá/mở kèm lý do (khoá phải
**cắt phiên đang mở ngay**, không chờ token hết hạn), đặt lại mật khẩu, xem nhật ký.

**VI.3. Người bán & shop** — duyệt đăng ký/KYC (xem giấy tờ từ bucket riêng tư qua URL ký có hạn),
cấp nhãn **Mall** / **Yêu thích**, khoá shop, điểm phạt (luật cộng điểm theo vi phạm, mức phạt theo
ngưỡng: hạn chế hiển thị, cấm tham gia chiến dịch, khoá), lịch sử.

**VI.4. Ngành hàng & sản phẩm** — cây danh mục kéo thả, thuộc tính theo ngành, thương hiệu; **hàng
đợi duyệt sản phẩm** (duyệt / từ chối / yêu cầu sửa kèm lý do), từ khoá cấm (tự gắn cờ khi đăng),
xử lý báo cáo vi phạm, khoá sản phẩm hàng loạt.

**VI.5. Đơn hàng & khiếu nại** — tra mọi đơn, xem toàn bộ lịch sử trạng thái/thanh toán/vận đơn;
**phân xử khiếu nại** trả hàng (mục 3.8); can thiệp có kiểm soát (huỷ, hoàn tiền thủ công — bắt buộc
lý do, ghi nhật ký, cần quyền riêng).

**VI.6. Marketing của sàn** — voucher sàn, khung Flash Sale & duyệt đăng ký, chiến dịch/landing,
banner & popup, lối tắt trang chủ, từ khoá hot thủ công, gửi thông báo hàng loạt theo phân khúc (có
giới hạn tần suất, một người không nhận trùng một chiến dịch).

**VI.7. Tài chính** — biểu phí theo ngành (có hiệu lực từ ngày), duyệt rút tiền, sổ cái, đối soát
với cổng thanh toán và hãng vận chuyển (tải tệp đối soát → khớp từng giao dịch → liệt kê lệch).

**VI.8. Nội dung & cấu hình** — trang tĩnh, trung tâm trợ giúp, tham số hệ thống (theo nhóm, có lịch
sử thay đổi), mẫu thư/SMS/thông báo, danh mục hành chính, đơn vị vận chuyển & cổng thanh toán (bật/tắt,
khoá).

**VI.9. Phân quyền & nhật ký** — vai trò (Quản trị cao nhất, Vận hành, Duyệt nội dung, CSKH, Kế toán,
Marketing), cây quyền `MODULE.ENTITY.ACTION`; nhật ký thao tác tự động qua EF interceptor, tra cứu lọc
theo người/hành động/đối tượng/thời gian, xem khác biệt cũ/mới, xuất Excel.

**VI.10. Báo cáo** — mỗi báo cáo đều có bảng + biểu đồ + xuất Excel/PDF, số liệu khớp truy vấn kiểm
chứng độc lập: GMV theo ngành/thời gian/tỉnh, top shop, top sản phẩm, hiệu quả voucher & Flash Sale,
tỉ lệ huỷ/hoàn theo shop & lý do, người dùng mới & quay lại, phễu chuyển đổi (xem → giỏ → đặt → trả tiền).

### PHÂN HỆ VII — THÔNG BÁO

- Interface `INotificationSender` × kênh: **trong ứng dụng** (bảng + SignalR), **email** (SMTP, mẫu
  HTML), **SMS** (interface, bản giả lập ghi ra bảng để demo OTP), **push** (FCM, chuẩn bị sẵn).
- Sự kiện tối thiểu: đặt hàng thành công, thanh toán thành công/thất bại, shop xác nhận, đang giao,
  giao thành công, sắp tự hoàn thành, huỷ, yêu cầu trả hàng & kết quả, hoàn tiền, voucher sắp hết hạn,
  sản phẩm yêu thích giảm giá/có hàng lại, tin chat mới; phía shop: đơn mới, yêu cầu huỷ, trả hàng,
  sản phẩm bị khoá, giải ngân, rút tiền.
- Gửi qua **outbox** (lưu cùng giao dịch nghiệp vụ, việc nền gửi sau) — không gửi giữa transaction.
- Tôn trọng `notification_prefs`; thông báo tổng hợp (khuyến mãi) tối đa 1 lần/ngày/người.

### PHÂN HỆ VIII — CHƯƠNG TRÌNH THÀNH VIÊN & XU

- Nhận xu: đánh giá đủ điều kiện, nhiệm vụ hằng ngày (điểm danh 7 ngày), hoàn xu từ voucher. Xu có hạn
  dùng; dùng xu khi thanh toán (mục 3.6); huỷ/hoàn đơn → trả xu đã dùng, thu hồi xu đã thưởng.
- Hạng thành viên (Bạc / Vàng / Kim cương) theo chi tiêu 6 tháng, quyền lợi = voucher riêng theo hạng.

---

## 6. YÊU CẦU PHI CHỨC NĂNG

### 6.1. Phân quyền & bảo mật
- Ba nhóm chủ thể tách bạch: **người mua**, **nhân viên shop** (theo `shop_staff`), **quản trị sàn**
  (RBAC). Mọi endpoint quản trị khai `[RequirePermission]`; `[Authorize]` một mình **không** phải là
  canh quyền — có phép thử quét mã nguồn bắt buộc mỗi endpoint chọn một trong ba lối.
- **Chống truy cập chéo (IDOR)**: mọi truy vấn đơn/địa chỉ/hội thoại/sản phẩm của shop lọc theo chủ
  sở hữu **ngay trong câu SQL**, trả 404 (không 403) khi không phải của mình. Phép thử: người mua A
  đọc/sửa đơn, địa chỉ, chat của B; nhân viên shop X sửa sản phẩm, đọc đơn của shop Y.
- BCrypt work factor ≥ 12; refresh token lưu băm, xoay vòng, phát hiện dùng lại → thu hồi cả chuỗi.
- HTTPS, HSTS, CSP, X-Frame-Options, X-Content-Type-Options — ở **mọi** tệp cấu hình Nginx.
- Giới hạn tốc độ: đăng nhập, OTP (theo SĐT + IP), đặt hàng, gửi chat, tìm kiếm; trả **429 JSON**
  tiếng Việt (cả tầng Nginx, không trả trang HTML 503).
- IP người dùng chỉ lấy từ `RemoteIpAddress` sau khi cấu hình ForwardedHeaders tin đúng dải proxy.
- Tải tệp: kiểm **chữ ký byte** (không tin `Content-Type`), giới hạn cỡ, xoá EXIF/GPS khỏi ảnh, lưu
  ngoài web root; ảnh KYC ở bucket riêng tư, chỉ phát URL ký có hạn.
- HTML mô tả sản phẩm, trang tĩnh lọc bằng HtmlSanitizer. Lọc ký tự U+0000 ở cửa vào (chuỗi truy vấn
  và thân JSON).
- Không ghi log mật khẩu, OTP, token, số thẻ, số tài khoản. Mã hoá cột nhạy cảm (số TK ngân hàng, CCCD).
- Dữ liệu cá nhân theo Nghị định 13/2023/NĐ-CP: đồng ý xử lý dữ liệu khi đăng ký, xuất & xoá theo yêu cầu.

### 6.2. Tính đúng khi đồng thời (bắt buộc có phép thử **thật sự song song**)
Mọi luật "chỉ một" phải có **ràng buộc ở CSDL**, không chỉ kiểm ở tầng nghiệp vụ:
một SKU không bán quá tồn; một voucher không dùng quá quota / quá lượt mỗi người; một checkout cho một
Idempotency-Key; một webhook xử lý một lần; một đánh giá cho một dòng đơn; một người theo dõi một shop
một lần; một SKU một chương trình giá tại một thời điểm; suất Flash Sale không vượt quota; một yêu cầu
trả hàng đang mở cho một dòng đơn; rút tiền không vượt số dư khả dụng.

### 6.3. Hiệu năng
- Trang chủ, danh mục, chi tiết sản phẩm: TTFB < 300 ms (cache Redis + CDN-ready); tìm kiếm < 500 ms
  với **1 triệu sản phẩm** (dữ liệu sinh để đo, mục 7).
- Chịu **1.000 người đồng thời** lúc mở Flash Sale (kịch bản k6 trong `e2e/load/`).
- Phân trang máy chủ toàn bộ, sắp xếp luôn kết thúc bằng khoá duy nhất; ảnh WebP 3 cỡ, lazy-load;
  nén gzip/brotli; bundle tách theo trang (giữ lazy-load hiện có).

### 6.4. Vận hành
- `/health`, `/health/ready` (DB, Redis, MinIO, Meili). Graceful shutdown.
- Hangfire Dashboard sau quyền quản trị. Việc nền có khoá chống chạy trùng và tự đóng lượt chết:
  nhả kho đơn quá hạn trả tiền, tự huỷ đơn shop không xử lý, tự hoàn thành đơn đã giao N ngày, giải
  ngân, hết hạn voucher/xu, đối chiếu thanh toán treo, đồng bộ Meili, tính lại chỉ số chép sẵn (điểm
  đánh giá, đã bán, người theo dõi), dọn giỏ khách cũ, sao lưu.
- Đổi tham số lịch chạy trên màn hình → **đăng ký lại ngay**, không đợi khởi động lại.
- Việc dài (nhập Excel, xuất báo cáo lớn) không chạy trong lượt HTTP — xếp hàng, trả mã việc, có tiến độ.

### 6.5. Giao diện
- `web/`: giữ phong cách Shopee hiện có (cam chủ đạo là **token**, không viết mã màu thẳng trong TSX).
  Phông hỗ trợ đủ dấu tiếng Việt (Be Vietnam Pro / Inter, đóng gói sẵn — **không** Georgia).
- Responsive: `web` đẹp ở **375 px** (điện thoại) tới 1920 px, không cuộn ngang ở bất kỳ trang nào;
  `seller`/`admin` tối thiểu 1366×768. Đo ở đúng hai khổ ấy.
- Trạng thái rỗng, đang tải (khung xương), lỗi — cho mọi danh sách. Toast, hộp xác nhận cho xoá/huỷ.
- Bàn phím đi được, tương phản WCAG AA — có phép thử đo cặp màu.

### 6.6. SEO (site người mua)
- Trang sản phẩm, danh mục, shop, tìm kiếm phải có **HTML render sẵn** cho máy thu thập (SSR/prerender
  qua Vite SSR hoặc một lớp render cho bot ở Nginx): `<title>`, meta description, Open Graph (ảnh
  sản phẩm), **JSON-LD `Product` + `Offer` + `AggregateRating`**, canonical, breadcrumb.
- `sitemap.xml` (chia tệp ≤ 50.000 URL), `robots.txt`, URL thân thiện có dấu gạch.

### 6.7. Ứng dụng di động — **đợt sau**
Không build trong đợt này. Nhưng mọi API là REST + JWT, không phụ thuộc phiên máy chủ, nên app Flutter
cắm vào được. Chuẩn bị sẵn `device_tokens` và kênh push.

---

## 7. DOCKER & DỮ LIỆU MẪU

`docker-compose.yml`: `postgres`, `redis`, `minio`, `meilisearch`, `api`, `web`, `seller`, `admin`,
`nginx` (route `/` → web, `/seller` → seller, `/admin` → admin, `/api` + `/hubs` → api; `resolver
127.0.0.11` + tên dịch vụ trong biến, không `upstream` ghim IP), `mailpit` (bắt thư khi dev).
`docker-compose.prod.yml`: HTTPS, giới hạn tài nguyên, restart, log driver. `.env.example` chú thích
tiếng Việt. Kịch bản triển khai **tự dọn ảnh Docker cũ**, giữ bản đang chạy và bản trước.

**Dữ liệu gieo** (chạy `docker compose up -d` là có ngay, mọi mốc thời gian **tính từ ngày nạp**,
không viết cứng ngày; mỗi phần dữ liệu có rào kiểm **chính phần ấy** đã có chưa):
- Tài khoản: `admin` (quản trị, buộc đổi mật khẩu lần đầu), 3 chủ shop, 2 nhân viên shop, 20 người mua;
  mật khẩu mẫu sinh ngẫu nhiên in ra log lần đầu, **không** ghi vào repo.
- Danh mục hành chính Việt Nam đầy đủ (tỉnh/quận/phường), 18 ngành hiện có → cây 3 cấp + thuộc tính.
- 30 shop (gồm 6 thương hiệu Mall hiện có: TechZone, Fashionista, HomeLux, BeautyPro, SportKing, BabyCare),
  40 sản phẩm hiện có **chuyển thành dữ liệu thật** (giữ tên, ảnh, phân loại) + sinh thêm tới ~1.000
  sản phẩm, ~4.000 SKU.
- 500 đơn hàng trải mọi trạng thái trong 90 ngày, **đi qua đúng máy trạng thái và PricingEngine** (không
  chèn thẳng bảng) để số liệu đã bán, đánh giá, sổ cái khớp nhau; 800 đánh giá chỉ trên đơn đã hoàn thành.
- Voucher mẫu (`SHOPHUB50`, `FREESHIP`, `SALE12` + voucher shop), 1 khung Flash Sale đang chạy và 1 sắp
  tới, 3 banner, 1 chiến dịch, trang tĩnh pháp lý đầy đủ chữ.
- Lệnh riêng sinh **1 triệu sản phẩm** để đo hiệu năng (`SH_SEED=perf`), không chạy mặc định.

---

## 8. LUẬT LÀM VIỆC TRÊN KHO MÃ

**Kiểm thử.** Sửa lỗi nào cũng kèm phép thử **đỏ trước khi sửa, xanh sau khi sửa**. Phép thử đồng
thời phải gửi yêu cầu **song song thật**; gọi tuần tự là không bao giờ thấy lỗi.

```bash
cd backend && dotnet test
cd web     && npx tsc -b && npx vitest run
cd seller  && npx tsc -b && npx vitest run
cd admin   && npx tsc -b && npx vitest run
cd e2e     && npx playwright test          # gồm 19 kịch bản cũ đã chuyển sang dữ liệu thật
```

**Phép thử quét mã nguồn** (chặn cả một lớp lỗi; nhớ chạy cho **cả ba** gói frontend):
| Phép thử | Luật |
|---|---|
| `EndpointAuthorisationTests` | Mỗi endpoint: `[RequirePermission]` / `[AllowAnonymous]` / có tên trong danh sách "tự canh quyền chủ sở hữu" kèm lý do |
| `OrderStatusWriteTests` | Không chỗ nào gán `order.Status =` ngoài `OrderStateMachine` |
| `MoneyTypeTests` | Không `decimal`/`double` cho tiền trong Domain/Application; mọi tính giá đi qua `PricingEngine` |
| `StablePagingOrderTests` | Mọi danh sách phân trang kết thúc chuỗi sắp xếp bằng khoá duy nhất |
| `SystemParameterReadersTests` | Mỗi khoá tham số được gieo phải có chỗ **đọc** trong mã — công tắc lưu mà không ai đọc là công tắc chết |
| `palette.test.ts` (×3) | Không viết mã màu thẳng trong TSX; màu đi qua token |
| `datetime.test.ts` (×3) | Không tự định dạng ngày giờ ngoài `lib/datetime` |
| `api-paths.test.ts` (×3) | Ngoài `src/api` không viết chuỗi `/api/...` |
| `NginxConfigParityTests` | Mọi tệp Nginx cùng mang tiêu đề bảo mật, `limit_req_status 429`, trang lỗi JSON, `resolver` |
| `MigrationRegistrationTests` | Mỗi migration có `[Migration("…")]` — thiếu là EF bỏ qua trong im lặng |

**Những bẫy đã biết — đừng mắc:**
1. Kiểm "còn hàng không" rồi mới ghi là hai người cùng mua được món cuối. Ràng buộc ở CSDL hoặc
   UPDATE có điều kiện (mục 3.3, 6.2).
2. Cột chép sẵn (`sold_count`, `rating_avg`, `follower_count`, số dư…) phải được **tính lại từ nguồn**,
   không cộng dần, và gọi ở **mọi** lối làm nó đổi. Bộ gieo dữ liệu không được **gán** con số tích luỹ.
3. Đếm (`totalCount`) và lấy dòng phải chạy trên cùng một tập; điều hướng bắt buộc trong `Select`
   thành INNER JOIN và bộ lọc xoá mềm lặng lẽ cắt dòng. Xoá mềm cha là mất con khỏi mọi danh sách —
   truy vấn cần thấy mọi dòng (lịch sử đơn của sản phẩm đã xoá) dùng `IgnoreQueryFilters()`.
4. Lọc trong SQL rồi mới phân trang; không "lấy 500 dòng rồi lọc".
5. Sắp giảm dần cột có thể rỗng → ô trống lên đầu; khai "rỗng sau cùng".
6. Khoảng ngày ngược (từ > đến) → báo lỗi rõ ở **một** chỗ trong đường ống, không trả bảng rỗng im lặng.
7. Lệnh nhận mảng (in phiếu hàng loạt, xác nhận đơn hàng loạt, cập nhật giá hàng loạt) phải có **trần**.
8. Gửi thông báo/email phải hỏi kênh có bật không; tắt thì không báo "đã gửi".
9. Khoá tài khoản phải cắt phiên đang mở (kiểm trong `OnTokenValidated`, đệm ngắn, xoá đệm khi khoá).
10. Mốc giờ hiện cho người dùng luôn qua giờ Việt Nam — kể cả nhãn kỳ của biểu đồ.
11. "Đã lưu" chưa phải "hiện ra được": kiểm tới **chỗ người dùng nhìn thấy** (ảnh hiện trên trang,
    không chỉ có tệp trong MinIO).
12. Tệp PDF xuất ra: chữ rút lại phải bằng chữ ghi vào (tắt ghép chữ của phông).
13. Thêm luật mới cho một gói frontend thì hỏi ngay hai gói kia có vi phạm không.
14. Test xanh không có nghĩa là đúng. Mở hệ thống ra dùng như người thật, cố tình đi đường sai, gọi
    thẳng API bỏ qua giao diện.

**Migration.** Đặt tên có nghĩa, không sửa migration đã commit; sửa lỗi dữ liệu thì kèm migration dọn
dữ liệu cũ. Sổ quyết định `docs/00-quyet-dinh-ky-thuat.md` ghi mọi chỗ tự chốt khi đặc tả không nói rõ.
Sổ lỗi `docs/08-so-loi.md` ghi thẳng, có bằng chứng.

**Định dạng API thống nhất:** `{ "success": true, "data": {}, "message": "", "errors": [] }`; phân
trang `{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }`; lỗi kiểm tra 400 có danh sách
lỗi theo trường; xung đột nghiệp vụ 409; mọi thông báo bằng tiếng Việt, không lọt tiếng Anh của khung
nền. Swagger đầy đủ, có ví dụ.

**Quy tắc code:** comment & tên biến tiếng Anh, chuỗi hiển thị tiếng Việt (tệp i18n). Không `any`.
Controller mỏng, không try-catch rải rác. Mỗi phase xong: build sạch không cảnh báo, test xanh, cập
nhật `README.md` và `docs/`.

---

## 9. KIỂM THỬ ĐẦU-CUỐI BẮT BUỘC (Playwright, trên dữ liệu thật)

Giữ 19 kịch bản hiện có, thêm tối thiểu:
1. Đăng ký bằng SĐT + OTP (đọc mã từ bộ gửi SMS giả lập) → đăng nhập → thêm địa chỉ.
2. Khách chưa đăng nhập thêm giỏ → đăng nhập → giỏ được gộp.
3. Giỏ 2 shop + voucher shop + voucher sàn + xu → đặt hàng → **2 đơn**, tổng tiền khớp từng đồng với
   PricingEngine; COD.
4. Thanh toán online qua `SimulatedGateway`: thành công / thất bại / bỏ đi → kho được nhả đúng lúc.
5. Người bán xác nhận → in phiếu giao (PDF hợp lệ) → hãng giả lập đẩy trạng thái → người mua "Đã nhận"
   → đánh giá có ảnh → điểm sản phẩm đổi → shop thấy tiền chờ giải ngân → giải ngân → rút tiền.
6. Huỷ trước xác nhận / yêu cầu huỷ sau xác nhận / shop từ chối.
7. Trả hàng một phần → shop từ chối → khiếu nại → quản trị phân xử → hoàn đúng số tiền sau phân bổ giảm giá.
8. Flash Sale: 30 phiên trình duyệt song song tranh 10 suất → đúng 10 đơn.
9. Người bán đăng sản phẩm 2 tầng phân loại → quản trị duyệt → tìm thấy bằng từ khoá **không dấu** →
   lọc facet đếm đúng.
10. Chat người mua ↔ shop thời gian thực (hai phiên trình duyệt).
11. IDOR: người mua A mở đơn của B → 404; nhân viên shop X sửa sản phẩm shop Y → 404.
12. Quản trị khoá người mua đang đăng nhập → yêu cầu kế tiếp của người ấy bị từ chối.
13. Mọi trang `web` ở 375 px không cuộn ngang; `seller`/`admin` ở 1366×768.

---

## 10. TÀI LIỆU BÀN GIAO (`docs/`, tiếng Việt, có ảnh chụp)

`00-quyet-dinh-ky-thuat.md`, `01-huong-dan-nguoi-mua.md`, `02-huong-dan-nguoi-ban.md`,
`03-huong-dan-quan-tri.md`, `04-cai-dat-van-hanh.md` (hạ tầng, biến môi trường, HTTPS, sao lưu/phục
hồi, giám sát), `05-api-reference.md` (gồm chương webhook thanh toán & vận chuyển, và chương "API cho
ứng dụng di động"), `06-kich-ban-kiem-thu.md` (Mã | Chức năng | Bước | Mong đợi | Thực tế | Đạt),
`07-bang-doi-chieu-chuc-nang.md` (từng mục của tài liệu này → màn hình/endpoint → bằng chứng),
`08-so-loi.md`.

---

## 11. THỨ TỰ THỰC HIỆN (tuần tự, không nhảy bước; mỗi phase có tiêu chí nghiệm thu)

**Phase 0 — Chuyển đổi repo.** Dời `src/` hiện có vào `web/`, chuyển sang TypeScript (giữ nguyên giao
diện và 19 e2e xanh), dựng khung `backend/`, `seller/`, `admin/`, `e2e/`, docker-compose với postgres/
redis/minio/meili/mailpit. Đổi các id `shopee-*` trong dữ liệu.
→ *`docker compose up -d` lên đủ dịch vụ; `web` trông y như cũ; 19 e2e xanh.*

**Phase 1 — Nền móng backend.** Clean Architecture, EF + migration, xử lý ngoại lệ tập trung, định
dạng API, Serilog, health check, audit interceptor, tham số hệ thống, outbox, Hangfire, phép thử quét
mã nguồn mục 8.

**Phase 2 — Tài khoản (Phân hệ I).** Đăng ký/đăng nhập/OTP/refresh/thiết bị, hồ sơ, sổ địa chỉ +
danh mục hành chính, RBAC quản trị, khung `admin` có đăng nhập và menu theo quyền.
→ *Thay `AuthContext` giả bằng đăng nhập thật; mật khẩu sai bị từ chối.*

**Phase 3 — Danh mục & sản phẩm.** Cây danh mục + thuộc tính, thương hiệu, sản phẩm + phân loại 2
tầng + SKU + tồn kho, ảnh lên MinIO; đăng ký shop + KYC + duyệt; Kênh Người Bán phần sản phẩm; quản
trị duyệt sản phẩm. Gieo 40 sản phẩm cũ thành dữ liệu thật.

**Phase 4 — Tìm kiếm & trang người mua.** Meili + outbox đồng bộ, gợi ý, facet; trang chủ, danh mục,
tìm kiếm, chi tiết, trang shop, theo dõi, yêu thích, đã xem — tất cả từ API. Bỏ `src/data/products.js`.
→ *Tìm "dien thoai" ra "Điện Thoại…"; số facet khớp SQL kiểm chứng.*

**Phase 5 — Giỏ hàng & thanh toán (lõi, làm kỹ nhất).** Giỏ theo shop, gộp giỏ khách, PricingEngine,
voucher sàn & shop, xu, vận chuyển (`SimulatedCarrier` trước), checkout tách đơn, idempotency, giữ kho,
COD + `SimulatedGateway`, webhook đúng-một-lần, nhả kho quá hạn.
→ *Phép thử song song tồn kho/voucher xanh; e2e số 3, 4 xanh.*

**Phase 6 — Đơn hàng & vận chuyển.** Máy trạng thái, Đơn mua phía người mua, xử lý đơn phía shop, in
phiếu giao hàng loạt, hành trình vận đơn, huỷ/yêu cầu huỷ, tự hoàn thành, thông báo theo sự kiện.

**Phase 7 — Đánh giá, trả hàng, khiếu nại.**

**Phase 8 — Tài chính.** Sổ cái, biểu phí, giải ngân, rút tiền, đối soát, Ví ShopHub.

**Phase 9 — Marketing.** Chương trình giảm giá, combo, mua kèm, quà tặng, Flash Sale sàn & shop (Redis
Lua + đối chiếu), chiến dịch, banner, popup, hạng thành viên, nhiệm vụ xu.
→ *e2e số 8 xanh; kịch bản k6 1.000 người đạt.*

**Phase 10 — Chat & thông báo thời gian thực.** SignalR, chat người mua ↔ shop, nhiều nhân viên, trả
lời nhanh; thông báo đẩy trong app; email qua mailpit.

**Phase 11 — Cổng thật.** VNPay & MoMo sandbox, GHN & GHTK sandbox (bật bằng khoá trong `.env`; không
có khoá thì giả lập vẫn chạy).

**Phase 12 — Quản trị & báo cáo.** Đủ Phân hệ VI, báo cáo shop & sàn (bảng + biểu đồ + Excel/PDF),
nhật ký.

**Phase 13 — Hoàn thiện.** SEO (SSR/prerender, JSON-LD, sitemap), hiệu năng 1 triệu sản phẩm, rà bảo
mật (IDOR, giới hạn tốc độ, tải tệp), đo 375 px & 1366 px, WCAG AA, docker-compose.prod, sao lưu/phục
hồi, đủ tài liệu `docs/`, chạy toàn bộ kịch bản `docs/06`.
→ *`docker compose up -d` là có một sàn chạy trọn vòng: đăng ký → mua → giao → đánh giá → giải ngân.*

**Phase 14 — Ứng dụng di động (đợt sau).** Flutter dùng chung API.

---

## 12. NGOÀI PHẠM VI ĐỢT NÀY (ghi rõ để không ai tưởng là đã có)

Livestream bán hàng, video ngắn, trò chơi nhận xu (ngoài điểm danh), tìm bằng hình ảnh, quảng cáo trả
tiền theo lượt nhấp cho shop, bảo hiểm sản phẩm, tín dụng "mua trước trả sau" tự vận hành, hoá đơn điện
tử VAT (tích hợp nhà cung cấp hoá đơn — để interface), chương trình tiếp thị liên kết (affiliate), bán
hàng xuyên biên giới, kho fulfillment của sàn. Muốn làm mục nào thì thêm một phase riêng **sau** Phase 13.

---

## 13. LƯU Ý CUỐI

1. **Không stub.** Cổng ngoài chưa có khoá → dùng bản giả lập chạy đúng vòng đời, không hàm rỗng.
2. **Tiền là số nguyên, tính ở một chỗ (`PricingEngine`), phân bổ không lệch một đồng.**
3. **Tồn kho, voucher, suất Flash Sale: ràng buộc ở CSDL, phép thử song song thật.**
4. **Trạng thái đơn chỉ đổi qua máy trạng thái**, mọi lần đổi có lịch sử.
5. **Webhook xử lý đúng một lần**; trang "quay về" của cổng không phải bằng chứng đã trả tiền.
6. **Mọi con số hiện cho người dùng (đã bán, sao, theo dõi, số dư) phải truy được về dữ liệu gốc.**
7. Mỗi phase xong tự đối chiếu `docs/07` và cập nhật; ghi lỗi thẳng vào `docs/08`.

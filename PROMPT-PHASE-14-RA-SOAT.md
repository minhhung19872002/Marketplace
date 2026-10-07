# ShopHub — PHASE 14: VÁ CÁC CHỖ CÒN THIẾU SAU ĐỢT RÀ SOÁT ĐỘC LẬP

> Đặt tệp này ở gốc repo (cạnh `PROMPT-BUILD-SHOPHUB.md`) và bảo Claude Code:
> *"Đọc PROMPT-PHASE-14-RA-SOAT.md, làm lần lượt từ nhóm A tới nhóm G."*
>
> Đợt rà ngày 07/10/2026 đi theo đặc tả, đọc lần lượt từng gạch đầu dòng của `PROMPT-BUILD-SHOPHUB.md` rồi tìm
> bằng chứng trong mã (commit `ea48b76`). Chỉ hỏi "mã có làm đúng thứ người viết nghĩ không" là không đủ.
> `docs/07` ghi Phase 0–13 **Xong** và 69/69 e2e xanh, nhưng đợt rà vẫn tìm ra **~60 chỗ** sai hoặc thiếu.
> Đa số nằm ở khoảng giữa hai lớp: backend có mà giao diện không gọi, tham số có mà một nửa lối đi không đọc,
> bảng chỉ có số dư chép sẵn chứ chưa có bước tính lại.
>
> Mọi đường dẫn `tệp:dòng` dưới đây là bằng chứng lúc rà. Mở ra xem lại trước khi sửa, vì dòng có thể đã xê dịch.

---

## 0. LUẬT CỦA PHASE NÀY

1. **Mỗi mục là một lỗi.** Ghi vào `docs/08-so-loi.md` theo mã tiếp theo (L063…). Kèm phép thử **đỏ trước khi
   sửa, xanh sau khi sửa**. Không dựng được bước đỏ thì phải tự dựng bối cảnh, chưa dựng được thì chưa coi là đã sửa.
2. **Sửa cả lớp, không sửa một chỗ.** Mục nào ghi *"quét"* thì thêm luật vào bộ phép thử quét mã nguồn
   (`backend/tests/**/SourceScan/CodeRuleTests.cs` hoặc `src/test/*.test.ts` của **cả ba** gói frontend).
3. **Sửa xong `docs/07` cho thật.** Mục nào chưa xong thì ghi "Một phần" hoặc "Chưa", không ghi "Xong".
4. **Dữ liệu cũ.** Lỗi nào đã để lại hậu quả trong CSDL (số dư lệch, đơn đã bị huỷ oan…) thì kèm migration
   hoặc việc nền dọn dữ liệu, và ghi con số trước/sau vào sổ lỗi.
5. **Không mở rộng phạm vi.** Mục 12 của đặc tả vẫn ngoài phạm vi. Nhóm G là việc cần người dùng cung cấp tài
   khoản ngoài: chỉ chuẩn bị, không bịa kết quả.

---

## A. TIỀN VÀ NGHIỆP VỤ — LÀM TRƯỚC (P0)

**A1. Số dư sổ cái là phép cộng dồn, không được tính lại (đặc tả 3.9, bẫy 2).**
`Ledger.cs:104` ghi `balance = balance + {delta}`. `LedgerCheckService` (`SettlementService.cs:117-144`) chỉ ghi
log khi lệch mà không sửa. Chú thích `Domain/Finance/Ledger.cs:55` và mô tả tham số `ParameterKeys.cs:293` đều nói
"tính lại số dư từ bút toán", tức là **sai sự thật**.
→ Giữ UPDATE có điều kiện để chặn rút quá số dư (đúng). Thêm: việc kiểm định kỳ **tính lại** `balance` từ
`ledger_entries` và ghi đè; khi lệch thì báo quản trị qua thông báo, không chỉ ghi log. Thêm phép thử: làm lệch cột
bằng tay → chạy việc → số dư đúng bằng tổng bút toán. Sửa chú thích và mô tả tham số cho đúng.

**A2. Hạn giao và hạn chuẩn bị hàng bỏ qua ngày lễ — Tết là shop bị huỷ đơn oan và bị phạt.**
Chỉ ước tính lúc thanh toán (`ShippingCalculator.cs:45,66`) đọc danh sách ngày lễ. Ba chỗ còn lại truyền
`new HashSet<DateOnly>()`:
- hạn chuẩn bị hàng: `SellerOrderFeatures.cs:147`, `OrderLifecycle.cs:369` (tự huỷ + điểm phạt);
- ngày giao dự kiến của vận đơn: `SellerOrderFeatures.cs:260`;
- ngày dự kiến của hàng trả về: `ReturnFeatures.cs:194`.

Ngoài ra:
- Chủ nhật đang viết cứng (`VietnamTime.cs:23`);
- `LOGISTICS.HOLIDAYS` chỉ gieo ngày của năm 2026 (`ParameterKeys.cs:238`).

→ Một dịch vụ `IWorkingCalendar` duy nhất: đọc tham số ngày lễ cùng tham số ngày nghỉ trong tuần, có đệm ngắn.
Mọi lối gọi `AddWorkingDays` đều qua nó.
→ Gieo ngày lễ cho 2026–2028 (Tết âm lịch tính sẵn). Màn quản trị cảnh báo khi danh sách không còn ngày nào của năm sau.
→ **Quét:** cấm truyền `new HashSet<DateOnly>()` hoặc tập rỗng vào `AddWorkingDays` ngoài phép thử.
→ Kiểm lại dữ liệu: có đơn nào bị tự huỷ hay shop nào bị cộng điểm phạt vì một ngày lễ đã qua không? Nếu có thì gỡ điểm.

**A3. Đối soát hãng vận chuyển không lọc theo hãng (VI.7).**
`AdminFinanceFeatures.cs:243-246` lấy **mọi** vận đơn COD đã giao trong kỳ. Đưa lên tệp của GHN là toàn bộ vận đơn
GHTK và giả lập bị báo "MissingInStatement". Ngoài ra, một dòng lặp hai lần trong tệp bị tính "khớp" hai lần.
→ Bắt buộc chọn hãng khi đưa tệp lên, rồi lọc theo `carrier_id`. Dòng trùng mã vận đơn trong tệp thì báo "Trùng
trong tệp". Phép thử: có hai hãng, đưa tệp của một hãng thì hãng kia không lọt vào kết quả.

**A4. Đối soát cổng thanh toán chỉ chạy cho cổng giả lập (VI.7).**
`AdminFinanceFeatures.cs:192` lọc `Method == Simulated`, và chỉ nhận CSV do chính ShopHub sinh ra. Màn hình ghi
thẳng "Cổng thanh toán (giả lập)" (`admin/src/pages/FinancePage.tsx:182`).
→ Chọn cổng (VNPay / MoMo / ZaloPay / giả lập), lọc theo `method`. Thêm bộ đọc cho định dạng tệp đối soát của từng
cổng, theo tài liệu công khai của cổng: cột, mã giao dịch, số tiền, phí. Có tệp mẫu trong `tests/fixtures`.
Tệp thật thì chờ nhóm G.

**A5. Hoàn tiền thủ công của sàn (VI.5).**
Hiện quản trị chỉ huỷ được đơn hoặc xử lý lại một lượt hoàn tiền hỏng (`PlatformAdminControllers.cs:99-117`).
→ Thêm "Hoàn tiền thủ công" cho đơn đã giao hoặc đã hoàn thành:
- chọn dòng và số tiền, không vượt phần đã trả sau phân bổ giảm giá (`ReturnPricing`);
- bắt buộc lý do và quyền `SALES.ORDER.INTERVENE`;
- ghi bút toán: sàn chịu hay shop chịu, chọn rõ;
- khoá phần giải ngân tương ứng.

Phép thử: hoàn hai lần liên tiếp không vượt tổng đã trả.

**A6. Điều chỉnh tồn kho không cùng giao dịch với dòng lịch sử (3.3).**
`ProductFeatures.cs:619-627`: `ExecuteUpdate` tự commit, rồi mới lưu `InventoryMovement`. Lưu dòng lịch sử hỏng thì
tồn kho vẫn đổi mà không còn dấu vết.
→ Bọc cả hai bước trong một transaction. **Quét:** mọi chỗ đổi `stock` / `reserved` phải ghi `inventory_movements`
trong cùng giao dịch.

**A7. Xu hết hạn dừng ở 5.000 người (VIII).**
`LoyaltyFeatures.cs:173-175` dùng `Distinct().Take(5_000)`, không sắp xếp, không loại người đã xử lý, nên quá 5.000
người là chạy lặp đúng nhóm cũ.
→ Xử lý theo lô có con trỏ, sắp theo khoá. Chỉ lấy dòng xu **chưa** bị trừ hết hạn: đánh dấu dòng đã xử lý, hoặc
lọc bằng NOT EXISTS. Chạy lô tới khi hết. Phép thử với 12.000 người.
Ngoài ra, thu hồi xu hoàn từ voucher dùng `Math.Floor` (`LoyaltyFeatures.cs:151`); đổi sang dư lớn nhất như mục 3.1.

**A8. Hạng thành viên tính sai chi tiêu (VIII).**
`LoyaltyFeatures.cs:35` cộng `GrandTotal`: có cả phí ship, và không trừ phần đã hoàn khi trả hàng.
→ Chi tiêu = tiền hàng đã trả − phần đã hoàn. Ghi định nghĩa vào `docs/00`.

**A9. Bộ đếm chưa đọc của chat cộng dồn (bẫy 2).**
`Domain/Engage/Chat.cs:59-67` viết `ShopUnread++` / `BuyerUnread++`.
→ Tính lại từ `messages` (đếm tin `read_at IS NULL` của phía kia) sau mỗi lần gửi hoặc đọc. Có phép thử gửi song song.

**A10. Thông báo hàng loạt (VI.6).**
- Giới hạn một lần/ngày và chống trùng là đọc-rồi-ghi (`NotificationFeatures.cs:134-145`): hai đợt gửi cùng lúc đều
  tới người nhận. → Thêm ràng buộc duy nhất `(user_id, category, ngày VN)` cho thông báo khuyến mãi, và
  `(campaign_key, user_id)`.
- Phân khúc Vàng/Kim cương (`:127-130`) không lọc tài khoản bị khoá hay đã xoá. Phân khúc "Vàng" đang gồm cả Kim
  cương → đổi thành "đúng hạng" hoặc "từ hạng X trở lên", và ghi rõ trên giao diện.
- Nhắc "voucher sắp hết hạn" bị đếm chung hạn mức với tin quảng cáo, nên mất khi hôm ấy đã có một đợt gửi
  (`ReminderService.cs:88-93`). → Tách thành loại nhắc việc, không chung hạn mức với tin quảng cáo.
- Người dùng không tắt được thông báo khuyến mãi **trong ứng dụng** (`NotificationFeatures.cs:33,50`). → Cho phép tắt
  loại Khuyến mãi; loại Đơn hàng, Ví, Hệ thống thì vẫn bắt buộc.

**A11. Phễu chuyển đổi (VI.10).**
Mỗi bước đếm một tập người khác nhau (`AdminReports.cs:338-357`), nên tỉ lệ giữa hai bước có thể vượt 100%.
→ Đếm theo **một nhóm**: người có lượt xem trong kỳ, trong số ấy bao nhiêu thêm giỏ, đặt đơn, trả tiền. Đưa luôn
biểu đồ vào bản PDF (như ràng buộc "bảng + biểu đồ + tệp"). Phép thử đối chiếu bằng SQL độc lập.

---

## B. BẢO MẬT VÀ DỮ LIỆU CÁ NHÂN (P0)

**B1. Xoá tài khoản để lại liên kết Google, và lần đăng nhập Google sau sẽ hỏng.**
`DeleteMyAccountHandler` (`AccountFeatures.cs:310-326`) không xoá `UserIdentities`, tài khoản ngân hàng của ví,
`DeviceTokens`, và không chạy trong transaction. `GoogleLogin.cs:44-61` gặp liên kết trỏ tới người đã xoá: tạo người
mới nhưng không liên kết lại, nên lần sau đụng `ux_users_email`.
→ Xoá hoặc ẩn danh hoá cả ba bảng trong **một** transaction. `GoogleLogin` bỏ qua liên kết trỏ tới tài khoản đã xoá.
Có phép thử tích hợp cho cả xoá tài khoản lẫn "tải dữ liệu của tôi": hiện chưa có phép thử nào.

**B2. Rò rỉ khi tải tệp.**
- Video MP4 không được gỡ siêu dữ liệu, kể cả GPS (`MediaFeatures.cs:124-135`). → Gỡ bằng `ffmpeg -map_metadata -1
  -c copy`, hoặc từ chối video có thẻ vị trí.
- Bằng chứng khiếu nại và trả hàng (ảnh, video) đang vào bucket **công khai** `Reviews` (`MediaFeatures.cs:101,132`).
  → Chuyển sang bucket riêng tư `sh-returns`, chỉ phát URL ký có hạn cho người mua, shop và quản trị liên quan.
  Kèm migration chuyển tệp cũ.
- Gateway đặt `expires 7d` cho mọi đối tượng dưới `/s3/` (`gateway.conf:93-100`), kể cả ảnh KYC ký hạn. → Tệp riêng
  tư phải là `Cache-Control: private, no-store`.

**B3. `/assets/` trong `deploy/nginx/spa.conf:40-46` mất HSTS và CSP.**
Nginx không kế thừa `add_header` vào `location` có `add_header` của riêng nó. `NginxConfigParityTests` chỉ dò chữ
trong cả tệp nên không bắt được.
→ Đưa bộ tiêu đề bảo mật vào một tệp `include`. Nâng phép thử: **mọi khối `location` có `add_header`** phải
`include` tệp ấy.

**B4. `/health/ready` công khai và trả chi tiết lỗi nội bộ** (`Program.cs:79-83`, `HttpEndpointHealthCheck.cs:20`,
mở qua `gateway.conf:87`).
→ Ra ngoài gateway chỉ trả trạng thái tổng (Healthy/Unhealthy). Bản chi tiết chỉ cho mạng nội bộ hoặc quản trị.

**B5. Bảng điều khiển Hangfire không mở được bằng trình duyệt.**
Bộ lọc đọc JWT từ tiêu đề (`ApiServiceExtensions.cs:232-235`), mà trình duyệt mở trang thì không gửi tiêu đề ấy. Gói
`admin` cũng không có màn nào gọi `api/admin/job-runs`.
→ Admin xin một mã dùng một lần ngắn hạn (`POST /api/admin/jobs/ticket`) rồi mở dashboard kèm mã, đổi sang cookie
httpOnly gắn đường dẫn `/api/admin/jobs`. Thêm màn "Việc nền" trong admin: lần chạy gần nhất, lỗi, chạy lại.

**B6. Giới hạn tốc độ đăng nhập/OTP chỉ theo IP** (`ApiServiceExtensions.cs:152-157`). Đặc tả đòi theo IP **và**
theo tài khoản/SĐT.
→ Thêm phân vùng theo SĐT/email đã chuẩn hoá cho `/login`, `/login-otp`, `/otp/send`, `/forgot-password`. Kiểm lại
giới hạn theo SĐT trong `OtpService`; lượt rà chưa kiểm chỗ này.

**B7. Quyền nhỏ.**
- Lối gửi OTP thêm tài khoản ngân hàng kiểm quyền bằng tác dụng phụ của `ShopFinanceSummaryQuery` (FINANCE.VIEW)
  (`FinanceControllers.cs:136`), trong khi thêm tài khoản cần FINANCE.WITHDRAW. → Kiểm đúng quyền WITHDRAW.
- `EndpointAuthorisationTests` dùng `DeclaredOnly` nên bỏ qua action kế thừa (`:18`). → Bỏ cờ ấy.
- HtmlSanitizer cho `http:` ở `img src` (`MediaServices.cs:205`). → Chỉ `https:` và ảnh của chính sàn.
- Chỉ mục `ux_voucher_code` đang trên `code`, chưa trên `upper(code)` như đặc tả 4.10 (`SalesConfigurations.cs:209`).
- `NullCharacterMiddleware` không soi thân multipart/form.

---

## C. VẬN HÀNH (P1)

**C1. Không có CI.** Repo không có `.github/workflows`.
→ Thêm workflow `ci.yml`:
- `dotnet build -warnaserror`;
- `dotnet test` (Testcontainers trên runner Ubuntu);
- `npx tsc -b && npx vitest run` cho `web`, `seller`, `admin`;
- dựng stack Docker rồi chạy Playwright với `--workers=2`;
- tải lên báo cáo khi đỏ.

Chạy trên mỗi push và pull request. Các kịch bản cần tài khoản quản trị đọc từ GitHub Secrets; thiếu secret thì
**báo bỏ qua**, không im lặng.

**C2. Giờ chạy định kỳ (cron) đang tính theo UTC** (`JobScheduler.cs:108`). Người sửa trên màn tham số sẽ gõ theo giờ
Việt Nam. → Đặt `TimeZone = Asia/Ho_Chi_Minh` và chuyển giá trị cũ bằng migration (ví dụ `15 17 * * *` →
`15 0 * * *`). Mô tả tham số và màn hình ghi rõ "giờ Việt Nam", và hiện giờ chạy kế tiếp lấy từ Hangfire.

**C3. Sao lưu không nằm trong Hangfire và không chỉnh được từ màn hình** (đặc tả 6.4 liệt kê sao lưu). Hiện chỉ là
crond trong `docker-compose.prod.yml:119-132`; bản dev không có.
→ Đưa vào việc nền: lịch là tham số, số bản giữ lại, báo quản trị khi hỏng. Màn quản trị liệt kê các bản sao lưu
(tên, cỡ, thời điểm, trạng thái).

**C4. Ngày viết cứng trong bộ gieo:** `FinanceSeeder.cs:20` đặt biểu phí từ `2026-01-01`. → Tính từ ngày nạp.
**Quét:** cấm `new DateTime(20xx` / `DateOnly(20xx` trong thư mục `Seed/`.

**C5. Nâng các phép thử quét đang yếu.**
- `MoneyTypeTests` chỉ dò theo tên biến (`CodeRuleTests.cs:29-47`). Thêm vế "mọi tính giá đi qua `PricingEngine`":
  cấm cộng hay trừ `UnitPrice`/`ShippingFee` ngoài các tệp được phép.
- `OrderStatusWriteTests` chỉ bắt tên có chữ "order" (`:18`). Thêm phép thử phản chiếu: `Order.Status` không có
  setter public hoặc internal ngoài máy trạng thái.

**C6. Thiếu phép thử song song thật** (đặc tả 6.2).
- Mỗi luật sau cần một phép thử song song: một đánh giá cho một dòng đơn, hai chương trình giá trùng thời gian cho
  một SKU (ràng buộc EXCLUDE mới chỉ được thử tuần tự ở `MarketingTests.cs:138-145`), theo dõi shop, thông báo hàng
  loạt (A10).
- Gom các phép thử truy cập chéo (IDOR) của người mua, nhân viên shop và quản trị vào một tệp riêng, quét **mọi**
  endpoint có `{id}`/`{code}`.

---

## D. KÊNH NGƯỜI BÁN — CHỨC NĂNG CÓ BACKEND MÀ GIAO DIỆN KHÔNG GỌI (P2)

**D1. Liên kết từ Bảng điều khiển không có tác dụng.**
`ProductsPage.tsx:142` giữ tab trong `useState('All')` và không đọc `?tab=`. "Sản phẩm bị khoá"
(`/san-pham?tab=Banned`) và "Sắp hết hàng" (`?tab=LowStock`) mở tab "Tất cả" (`DashboardPage.tsx:18-19`). Thêm nữa,
`Banned` không phải tên tab hợp lệ; tên đúng là `Violation`.
→ Tab đồng bộ với URL (như `OrdersPage`). **Quét:** mọi `to=".../?tab=X"` trong seller phải là một tab có thật.

**D2. Số "sắp hết hàng" và tab "sắp hết hàng" dùng hai luật khác nhau.**
Bảng điều khiển đếm theo SKU `Stock-Reserved <= ngưỡng` (`SellerOrderFeatures.cs:561`); tab lọc theo tổng tồn của sản
phẩm (`ProductFeatures.cs:408-409`). → Dùng một biểu thức chung: sản phẩm có **ít nhất một SKU đang bán** khả dụng ≤
ngưỡng, đếm và lọc cùng một câu.

**D3. Shop bị từ chối không biết lý do và không gửi lại được.**
API `POST shops/{id}/resubmit` có (`SellerController.cs:31`) nhưng không màn nào gọi. `rejectReason` không hiện.
`App.tsx:98` chỉ xử lý trạng thái `PendingReview`. → Màn "Hồ sơ bị từ chối": lý do, sửa, gửi lại. Khoá menu bán hàng
khi shop chưa ở trạng thái Hoạt động.

**D4. "Thông báo của sàn" lẫn thông báo của chính người dùng** (`SellerOrderFeatures.cs:569-570`, điều kiện
`Category==Activity`). → Chỉ lấy tin do sàn phát (`RefType == broadcast` hoặc loại Hệ thống của sàn).

**D5. Ngày của báo cáo tài chính tính theo múi giờ trình duyệt** (`FinancePage.tsx:87` dùng
`dayjs.startOf('day').toISOString()`). → Dùng `lib/datetime` (giờ VN). **Quét:** cấm `startOf(`/`endOf(` ngoài
`lib/datetime` ở cả ba gói.

**D6. Bộ lọc và tuỳ chọn còn thiếu trên giao diện, dù backend đã có.**
- Sản phẩm:
  - lọc theo danh mục (`categoryId` có ở `SellerController.cs:256`, `api/seller.ts:210-215` không gửi);
  - **sao chép hàng loạt** (hiện chỉ sao chép từng sản phẩm);
  - lịch sử tồn kho có phân trang (đang dừng ở 50 dòng, `api/seller.ts:231`).
- Đơn hàng:
  - lọc ngày, đơn vị vận chuyển, phương thức thanh toán (`FulfilmentControllers.cs:28-31` có, `OrdersPage.tsx:141`
    không gửi);
  - xuất Excel mang theo **đủ** bộ lọc đang chọn (`OrdersPage.tsx:179`);
  - chọn khổ in A6/A5 (`OrdersPage.tsx:163` luôn in A6).
- Mã giảm giá của shop:
  - chọn sản phẩm hoặc danh mục áp dụng (`VouchersPage.tsx:71-72` đang ghi cứng `[]`);
  - sửa mã (`PUT vouchers/{id}` chưa có màn nào gọi);
  - phân trang (đang `pageSize=100`).
- Flash Sale của sàn: hiện cả tiêu chí **ngành hàng** (`CategoryIds`). Hiện shop bị từ chối vì một luật mà họ không
  nhìn thấy (`api/marketing.ts`, `MarketingPage.tsx:214,247`).
- Chương trình giảm giá và Flash Sale của shop: cho **sửa** khi chưa bắt đầu, không chỉ tạo hoặc dừng.
- Đăng ký bán hàng: chọn đơn vị vận chuyển (III.1). `RegisterShopCommand` chưa có trường này.
- Tài khoản ngân hàng của shop:
  - xoá và đổi tài khoản mặc định (chưa có endpoint phía shop);
  - danh sách ngân hàng lấy từ một danh mục ở máy chủ, không viết cứng ở hai chỗ (`api/finance.ts`,
    `RegisterShopPage.tsx:10-17`).
- Tài khoản phụ: hiện người được thêm vào ngay mà không hề đồng ý (`SellerController.cs:115-119`). → Gửi **lời
  mời**, người được mời chấp nhận thì mới vào; lời mời có hạn.

---

## E. SITE NGƯỜI MUA (P2)

**E1. Đăng ký bằng email** (I.1). Backend nhận email (`Identifiers.cs:51`), còn `Register.tsx:99-106` chỉ có ô SĐT.
→ Thêm lựa chọn "Email", xác minh qua thư (xem bằng Mailpit khi dev). Có phép thử e2e.

**E2. Header** (II.1).
- "❓ Hỗ Trợ" là `<span>` chết (`Header.tsx:89`). → Liên kết tới `/tro-giup`.
- Thêm "Tải ứng dụng" (trang giới thiệu, có QR; ứng dụng là đợt sau, ghi rõ "sắp ra mắt").
- Số trên biểu tượng giỏ phải là **số dòng** (`LineCount`), không phải tổng số lượng (`CartContext.tsx:78`).
- Xem nhanh giỏ: 5 dòng **mới thêm nhất** trên mọi shop, không phải 5 dòng đầu sau khi gộp theo shop
  (`Header.tsx:29,269`).

**E3. Trang chủ** (II.1).
- Dải lối tắt thiếu Mã giảm giá, Freeship, Deal sốc (`MarketingSeeder.cs:68-73`).
- Banner "Freeship mọi đơn" đang trỏ tới `sort=BestSelling` (`:67`); phải trỏ tới bộ lọc `freeship=true`.
- Khối **ShopHub Mall** mới chỉ có logo thương hiệu (`MallBrands.tsx:9-27`). → Thêm lưới sản phẩm Mall.

**E4. Chi tiết sản phẩm** (II.4).
- Phóng to ảnh (lightbox, vuốt trên điện thoại).
- Khối shop thiếu **điểm đánh giá shop**: `ShopSummaryDto` chưa có trường này (`ProductPageFeatures.cs:36,95`,
  `types.ts:122-137`). Áp cho cả trang shop.
- Chia sẻ Zalo bằng liên kết riêng, không chỉ qua `navigator.share`.
- "Mua ngay" phải sang thẳng thanh toán chỉ với dòng ấy (`ProductDetail.tsx:317` đang về giỏ).

**E5. Trang shop** (II.5).
- Thêm khối "Chương trình đang chạy": giảm giá, Flash Sale, combo của shop.
- Shop tạm nghỉ thì hiện **băng thông báo** rõ ràng ở đầu trang, không chỉ một dòng chữ nhỏ (`ShopPage.tsx:121-122`).

**E6. Giỏ hàng** (II.6). **Voucher shop theo từng khối** ngay trong giỏ (chọn hoặc lưu, xem ngay số tiền giảm),
đồng bộ với lựa chọn ở trang thanh toán.

**E7. Sổ địa chỉ:** ghim vị trí trên bản đồ, tuỳ chọn (I.2). Backend đã nhận Lat/Lng. Dùng Leaflet + OpenStreetMap,
không cần khoá.

**E8. Footer** (II.12). Liên kết mạng xã hội lấy từ tham số và có `href` thật; tham số trống thì ẩn biểu tượng
(`Footer.tsx:36-38`). "Flash Sale" trỏ tới trang Flash Sale (`Footer.tsx:30` đang trỏ `/`).

**E9. Trang danh mục:** banner dùng `<a href>` làm tải lại cả trang (`SearchResults.tsx:306`). → Dùng `BannerLink`.
**Quét:** cấm `<a href="/...">` nội bộ trong `web/src`.

---

## F. GIAO DIỆN CHUNG (P2, đặc tả 6.5–6.6)

**F1. Phông chữ.** `web/src/index.css:49` khai `'Roboto', 'Segoe UI', …`, mà Roboto không được tải, nên trang đang
hiện phông của hệ điều hành. → Đóng gói **Be Vietnam Pro** (`@fontsource/be-vietnam-pro`, các nét 400/500/600/700)
cho cả ba gói. Phép thử `theme`: `index.css` khai đúng phông ấy **và** phông ấy được import thật.

**F2. Hộp xác nhận cho xoá và huỷ.** Chưa có ở:
- xoá dòng giỏ (một dòng / nhiều dòng): `CartPage.tsx:199,211`;
- xoá địa chỉ: `AddressesPage.tsx:142`;
- xoá tài khoản ngân hàng: `WalletPage.tsx:207`.

→ Thêm một thành phần `ConfirmDialog` dùng chung cho `web`. **Quét:** mọi nút có nhãn "Xoá"/"Xóa"/"Huỷ"/"Hủy" phải
đi qua nó. Seller và admin dùng `Modal.confirm` / `Popconfirm`, quét tương tự.

**F3. Trạng thái lỗi của danh sách.** Các trang `CommercePages.tsx` (đơn, voucher, xu), `BrowsingPages.tsx`,
`Wishlist.tsx`, `Notifications.tsx`, `ShopPage.tsx` không xử lý `isError`, nên lỗi mạng hiện thành danh sách rỗng.
→ Thêm thành phần `QueryState` (đang tải / lỗi + Thử lại / rỗng) dùng chung. **Quét:** trang nào gọi `useQuery`
phải dùng nó.

**F4. Toast** cho mọi thao tác ghi thành công hoặc thất bại ở `web`, dùng một hệ thống chung. Hiện chỉ trang sản
phẩm có toast.

**F5. Trang 404.** `web/src/App.tsx` chưa có route bắt mọi đường dẫn còn lại. → Thêm trang "Không tìm thấy" có ô tìm
kiếm. Máy chủ cũng phải trả mã 404 cho bot (SEO).

**F6. Tiêu đề tab trình duyệt** (`document.title`) đổi theo từng trang: sản phẩm, shop, danh mục, tìm kiếm, đơn mua.
Hiện mọi trang dùng tiêu đề tĩnh trong `index.html`.

**F7. Mẫu nội dung thông báo** (VI.8). Hiện chỉ OTP, khung email và sự kiện đơn/sản phẩm sửa được
(`ContentFeatures.cs:110-135`). Các nội dung sau vẫn là chữ cố định trong mã:
- ví và rút tiền (`Withdrawals.cs:159-160`);
- nhắc việc (`ReminderService.cs:37-64`);
- thông báo hàng loạt;
- giải ngân;
- chat.

→ Đưa hết vào bảng mẫu. Tin SMS dùng tên sàn từ tham số, không viết cứng "ShopHub: " (`NotificationDelivery.cs:71`).
**Quét:** cấm truyền chuỗi tiếng Việt cố định làm `title`/`body` cho `INotificationSender`.

**F8. Từ khoá hot.** Vai trò Marketing không sửa được `SEARCH.HOT_KEYWORDS` (`Permissions.cs:122-123`). → Tách thành
màn riêng trong Marketing, có quyền riêng.

---

## G. CẦN NGƯỜI DÙNG CUNG CẤP — CHỈ CHUẨN BỊ, KHÔNG BỊA KẾT QUẢ

1. **Tài khoản sandbox VNPay, MoMo, ZaloPay, GHN, GHTK** (KB41 đang "Chưa chạy").
   → Viết sẵn `docs/04` mục "Chạy với sandbox thật": biến `.env` cần đặt, URL webhook cần khai với nhà cung cấp,
   kịch bản kiểm tay từng bước. Thêm lệnh `e2e/tools/sandbox-check.cjs` chạy một vòng thanh toán, IPN, hoàn tiền,
   vận đơn và webhook với khoá thật.
2. **Push FCM.** Hiện chỉ có `SimulatedPushSender`, ghi log là xong (`NotificationDelivery.cs:22-29`), và luôn được
   đăng ký (`DependencyInjection.cs:111`).
   → Cài `FcmPushSender` (HTTP v1, khoá tài khoản dịch vụ trong `.env`), chỉ bật khi có khoá. Token bị FCM trả
   `UNREGISTERED` thì xoá. Phép thử dùng máy chủ FCM giả trong tiến trình. `docs/07` ghi "chưa kiểm với Firebase thật".
3. **Nhà cung cấp SMS thật** (ví dụ eSMS, SpeedSMS). Giữ interface, thêm một bản cài, bật bằng khoá.

---

## H. NGHIỆM THU PHASE 14

- [ ] Mỗi mục A–F có mã lỗi trong `docs/08`, kèm phép thử đỏ → xanh.
- [ ] Các luật quét mới (A2, A6, C4, D1, D5, E9, F2, F3, F7) chạy ở đúng mọi gói liên quan.
- [ ] CI xanh trên `main`: backend, ba frontend, e2e trên stack Docker.
- [ ] `docs/07` cập nhật thật: nhóm G ghi "Chờ tài khoản", không ghi "Xong".
- [ ] Chạy lại toàn bộ kịch bản `docs/06` trên một bản cài **mới** (CSDL trắng) và ghi lại số đạt.
- [ ] Sau khi sửa hết, **rà lại một lượt theo đặc tả** đúng cách của đợt này: đọc từng gạch đầu dòng rồi tìm bằng
      chứng. Ghi những gì tìm thêm được vào `docs/08`.

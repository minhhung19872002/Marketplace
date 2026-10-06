# 07 — Bảng đối chiếu chức năng

Từng mục của đặc tả → nơi hiện thực → bằng chứng. Cập nhật sau mỗi phase.

## Tiến độ theo phase (mục 11)

| Phase | Nội dung | Trạng thái | Bằng chứng |
|---|---|---|---|
| 0 | Chuyển đổi repo | **Xong** | Xem bảng Phase 0 dưới đây |
| 1 | Nền móng backend | **Xong** | Xem bảng Phase 1 dưới đây |
| 2 | Tài khoản | **Xong** | Xem bảng Phase 2 dưới đây |
| 3 | Danh mục & sản phẩm | **Xong** (trừ mục ghi ở cuối bảng) | Xem bảng Phase 3 dưới đây |
| 4 | Tìm kiếm & trang người mua | **Xong** | Xem bảng Phase 4 dưới đây |
| 5 | Giỏ hàng & thanh toán | **Xong** (trừ mục ghi ở cuối bảng) | Xem bảng Phase 5 dưới đây |
| 6 | Đơn hàng & vận chuyển | **Xong** (trừ mục ghi ở cuối bảng) | Xem bảng Phase 6 dưới đây |
| 7 | Đánh giá, trả hàng, khiếu nại | **Xong** (trừ mục ghi ở cuối bảng) | Xem bảng Phase 7 dưới đây |
| 8 | Tài chính | **Xong** (trừ mục ghi ở cuối bảng) | Xem bảng Phase 8 dưới đây |
| 9 | Marketing | **Xong** (trừ mục ghi ở cuối bảng) | Xem bảng Phase 9 dưới đây |
| 10 | Chat & thông báo thời gian thực | Chưa làm | |
| 11 | Cổng thật | Chưa làm | |
| 12 | Quản trị & báo cáo | Chưa làm | |
| 13 | Hoàn thiện | Chưa làm | |

## Phase 0 — Chuyển đổi repo

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Dời `src/` vào `web/` | `web/src/**` (lịch sử git giữ qua `git mv`) | `git log --follow web/src/App.tsx` |
| Chuyển sang TypeScript | Mọi tệp `.tsx`/`.ts`, `strict: true`, không `any`; kiểu chung ở `web/src/types.ts` | `cd web && npx tsc -b` sạch |
| Giữ nguyên giao diện | Không đổi CSS ngoài tên biến token | 19 e2e xanh |
| Khung `backend/` | `ShopHub.sln`: Domain, Application, Infrastructure, Reporting, Api + UnitTests, IntegrationTests | `cd backend && dotnet build` (0 cảnh báo), `dotnet test` xanh |
| Khung `seller/`, `admin/` | Vite + React + TS + Ant Design 5, gọi `/health` thật | `npm run build` từng gói; trang hiện trạng thái API |
| Dời e2e vào `e2e/` | `e2e/e2e.spec.cjs`, `e2e/playwright.config.cjs` | `cd e2e && npx playwright test` → 19 passed |
| docker-compose đủ dịch vụ | `docker-compose.yml`: postgres, redis, minio (+ minio-init), meilisearch, mailpit, api, web, seller, admin, nginx | `docker compose up -d` → mọi dịch vụ healthy |
| Đổi id `shopee-*` | `mall`, `hang-4-sao`; token `--sh-primary` | `grep -ri shopee web/src` rỗng |

## Phase 1 — Nền móng backend

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Clean Architecture | Domain ← Application ← Infrastructure/Reporting ← Api | `ArchitectureTests` |
| MediatR + FluentValidation | `ValidationBehavior`, validator theo use case (`Features/<PhânHệ>/<UseCase>.cs`) | `Wrong_type_is_a_400_with_field_errors_in_vietnamese` |
| EF Core + migration, snake_case, xoá mềm, xmin | `ShopHubDbContext`, migration `InitialFoundation`, query filter `deleted_at IS NULL`, `Version` ↔ `xmin` | `Migration_creates_schemas_extensions_and_helpers`, `Soft_delete_keeps_the_row_hides_it_and_journals_it`, `Parallel_edits_with_the_same_version_let_exactly_one_win` |
| Tiền là số nguyên, chia theo dư lớn nhất (3.1) | `Domain/Common/Money.cs` | `MoneyTests` (5.000 lượt ngẫu nhiên, tổng không lệch 1 đồng) |
| Xử lý ngoại lệ tập trung, định dạng API thống nhất (8) | `GlobalExceptionHandler`, `StatusCodeEnvelope`, `ModelStateResponse` — `{ success, data, message, errors }`, 400/404/409 tiếng Việt | `Unknown_route_is_a_json_404`, `Malformed_json_never_leaks_framework_english`, `Unknown_parameter_is_404_and_stale_version_is_409` |
| Serilog → tệp + PostgreSQL | `Program.cs`, `LogTableColumns`, bảng `sys.logs` | bảng có sau migration |
| Health check | `/health` (sống), `/health/ready` (postgres, redis, minio, meilisearch) | `Readiness_reports_each_dependency`; qua gateway `http://localhost:18000/health/ready` |
| Audit interceptor | `AuditSaveChangesInterceptor` → `iam.audit_logs` (cũ/mới, che trường nhạy cảm), API tra cứu `GET /api/admin/audit-logs` | `Updating_a_parameter_writes_audit_and_outbox_in_the_same_transaction`, `Audit_log_search_rejects_inverted_range_and_pages_stably` |
| Tham số hệ thống | `sys.system_parameters`, `ParameterCatalog` (gieo theo từng khoá), `GET/PUT /api/admin/system-parameters`, `GET /api/site/info` | `Seeder_inserts_every_catalog_parameter_once`, `Site_info_is_public_and_comes_from_parameters` |
| Outbox | `sys.outbox_messages`, `EfOutbox` (cùng transaction), `OutboxDispatcher` (SKIP LOCKED), `OutboxCleanupJob` | `Outbox_dispatch_publishes_to_redis_and_marks_processed`, `Outbox_message_without_handler_is_retried_then_parked`, `Parallel_dispatchers_never_deliver_the_same_message_twice` (đỏ khi bỏ SKIP LOCKED: 150 lần gửi thay vì 30) |
| Hangfire (PostgreSQL storage) | `HangfireJobScheduler` đọc lịch từ tham số `JOB.*`, đăng ký lại ngay khi sửa | `hangfire.set` có `sys.outbox-dispatch`, `sys.outbox-cleanup`; job `Succeeded` trên stack |
| Bảo mật nền | JWT (issuer ShopHub), `[RequirePermission]`, ForwardedHeaders theo `SH_TRUSTED_PROXIES`, giới hạn tốc độ 429 JSON, lọc U+0000 | `Admin_endpoint_requires_token_then_permission`, `Expired_or_forged_tokens_are_rejected`, `Null_character_*` |
| Phép thử quét mã nguồn (8) | `EndpointAuthorisationTests`, `OrderStatusWriteTests`, `MoneyTypeTests`, `StablePagingOrderTests`, `SystemParameterReadersTests`, `NginxConfigParityTests`, `MigrationRegistrationTests`, `OutboxHandlerRegistrationTests`; `palette` / `datetime` / `api-paths` `.test.ts` × 3 gói | `dotnet test` (34 unit), `npx vitest run` ở `web`, `seller`, `admin` |

## Phase 2 — Tài khoản (Phân hệ I) + RBAC quản trị

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Đăng ký SĐT + OTP (6 số, 5 phút, sai ≤ 5, gửi lại sau 60 s) | `OtpService`, `SendOtp/VerifyOtp/Register` (`Features/Auth`), trang `/dang-ky` 3 bước | `Phone_otp_registration_then_password_login`, `Otp_resend_has_a_cooldown`, `Parallel_otp_guessing_never_exceeds_the_attempt_cap`, `One_ticket_registers_one_account_even_when_replayed_in_parallel`; e2e "Đăng ký bằng SĐT + OTP → đăng nhập → thêm địa chỉ" |
| Đăng ký bằng email + mã qua thư | Cùng luồng, đích là email → outbox `notify.email` → SMTP (Mailpit) | `OtpService.EnqueueDelivery` |
| Mật khẩu ≥ 8, có chữ và số; BCrypt ≥ 12 | `PasswordRules.StrongPassword`, `BCryptPasswordHasher.WorkFactor = 12` | `Registration_needs_consent_strong_password_and_a_valid_ticket` |
| Đăng nhập SĐT/email/tên đăng nhập + mật khẩu; đăng nhập OTP | `Login`, `LoginWithOtp`; trang `/dang-nhap` 2 tab | `Login_with_otp`; e2e "Đăng nhập cập nhật trạng thái header", "Sai mật khẩu bị từ chối" |
| Khoá tạm sau N lần sai, giới hạn tốc độ IP | `AUTH.MAX_FAILED_LOGIN`, `AUTH.LOCKOUT_MINUTES` (UPDATE nguyên tử); chính sách `auth`/`otp` | `Repeated_wrong_passwords_lock_the_account_temporarily`, `Parallel_wrong_passwords_still_trigger_the_lockout` |
| Quên mật khẩu qua OTP; đổi mật khẩu thu hồi token khác | `ResetPassword`, `ChangePassword`; trang `/quen-mat-khau`, `/tai-khoan/mat-khau` | `Forgot_password_resets_and_ends_every_session`, `Changing_password_signs_out_other_devices_only`, `Forgot_password_answers_the_same_for_unknown_numbers_and_sends_nothing` |
| JWT 15 phút + refresh xoay vòng, lưu băm, phát hiện dùng lại → thu hồi cả chuỗi | `SessionService.RotateAsync` | `Refresh_rotates_and_a_replayed_token_kills_the_whole_session`, `Parallel_refresh_of_one_token_succeeds_once`, `Refresh_also_works_from_the_httponly_cookie` |
| Thiết bị đăng nhập, đăng xuất từ xa | `GET/DELETE /api/account/sessions`, trang `/tai-khoan/thiet-bi` | `Devices_list_and_remote_logout_with_ownership`, `Logout_ends_the_session_immediately` |
| Hồ sơ; đổi SĐT/email qua OTP | `UpdateProfile`, `RequestContactChange/ConfirmContactChange`, trang `/tai-khoan/ho-so` | `Profile_update_and_phone_change_via_otp` |
| Sổ địa chỉ (Tỉnh → Quận → Phường, mặc định, ≤ 10) | `iam.addresses` (chỉ mục duy nhất một mặc định/người), `AddressFeatures`, trang `/tai-khoan/dia-chi` | `First_address_becomes_default_and_hierarchy_is_validated`, `Address_limit_is_enforced`, `Parallel_set_default_leaves_exactly_one_default`, `Deleting_the_default_promotes_another_address`, `Someone_elses_address_is_404_for_every_action` |
| Danh mục hành chính | `iam.admin_divisions` (10.810 đơn vị), `GET /api/admin-divisions` | `Administrative_divisions_are_complete_and_hierarchical` |
| Quyền riêng tư: tải dữ liệu, xoá tài khoản | `GET /api/account/export`, `POST /api/account/delete` (ẩn danh hoá, `IAccountDeletionGuard`) | `Deleting_the_account_anonymises_it_and_frees_the_number` |
| RBAC quản trị | `iam.roles/permissions/role_permissions/user_roles`, `PermissionCatalog`, `RoleCatalog` (6 vai trò), API `/api/admin/roles`, `/permissions`, `/users` | `Role_permissions_reach_the_token_and_the_last_super_admin_is_protected`, `Admin_user_search_pages_and_filters_in_sql` |
| Khoá người dùng cắt phiên ngay (e2e 12) | `LockUser` + `CachedSessionValidator` trong `OnTokenValidated` | `Locking_a_signed_in_user_rejects_their_very_next_request` |
| Quản trị buộc đổi mật khẩu lần đầu | claim `pcr` + `PasswordChangeGateMiddleware` | `Seeded_admin_must_change_password_before_doing_anything_else` |
| Khung `admin` có đăng nhập, menu theo quyền | `admin/src/App.tsx` (menu lọc theo `permissions`), trang Người dùng / Vai trò & quyền / Tham số / Nhật ký | e2e `admin.spec.cjs`; kiểm thủ công 1366×768 (đăng nhập → buộc đổi mật khẩu → menu đủ 5 mục → khoá/mở khoá → nhật ký) |
| Thay `AuthContext` giả | `web/src/context/AuthContext.tsx` + `stores/auth.ts` (Zustand) + `api/http.ts` (tự làm mới 401, gộp một lượt) | 21 e2e xanh trên dev, 26 trên stack |
| Chưa làm trong Phase 2 (xem 00 #30) | Ảnh đại diện, tài khoản ngân hàng/thẻ, cài đặt thông báo, Google | — |

## Phase 3 — Danh mục & sản phẩm

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Cây danh mục 3 cấp + thuộc tính theo danh mục lá | `catalog.categories` (CHECK cấp 1–3), `catalog.category_attributes` (chọn một / nhiều / chữ / số + đơn vị, bắt buộc, lọc được); quản trị: trang **Ngành hàng** | `Category_tree_is_public_and_admin_cannot_create_cycles_or_a_fourth_level`, `Required_attributes_and_leaf_category_are_enforced` |
| Thương hiệu | `catalog.brands`, `GET /api/brands`, quản trị thêm/sửa | trang Ngành hàng → Thương hiệu |
| SPU + 2 tầng phân loại + SKU (giá, giá gốc, tồn, mã, cân nặng) | `Product.SetVariants` (≤ 2 tầng × ≤ 20 lựa chọn, tổ hợp đủ, SKU giữ định danh) | `Two_tier_product_lifecycle_from_draft_to_selling`, `Editing_keeps_sku_identity_and_sensitive_edits_need_re_review` |
| Tồn kho `stock` / `reserved`, CHECK ở CSDL, UPDATE có điều kiện, `inventory_movements` | `ck_skus_stock`, `ck_skus_reserved`, `AdjustStockHandler` | `Parallel_stock_decrements_never_go_below_zero` (25 yêu cầu song song, tồn 10 → đúng 10 thành công), `Stock_cannot_drop_below_reserved_even_by_direct_sql` |
| Trạng thái NHÁP → CHỜ DUYỆT → ĐANG BÁN ⇄ ẨN, BỊ KHOÁ, ĐÃ XOÁ; duyệt lại khi sửa trường nhạy cảm | Phương thức của `Product` (không gán `Status` từ ngoài) | `Reject_needs_a_reason_and_ban_unban_round_trip`, `Editing_keeps_sku_identity…` |
| Ảnh lên MinIO, 3 cỡ WebP; video ≤ 30 s | `UploadMediaHandler`, `SkiaImageProcessor`, `Mp4VideoInspector`, `MinioObjectStorage` | `Uploaded_image_is_reencoded_to_three_webp_sizes_and_is_actually_downloadable` (tải về được qua URL), `Exif_and_gps_metadata_are_removed`, `File_type_comes_from_the_bytes_not_the_declared_content_type` |
| Mô tả HTML đã lọc XSS | `HtmlSanitizerAdapter` (danh sách cho phép) | `Description_html_is_sanitised` |
| Đăng ký shop + KYC + duyệt | `POST /api/seller/shops`, `shop.shops/shop_kyc/shop_warehouses/shop_staff/shop_bank_accounts`; admin trang **Shop** (URL ký 5 phút) | `Shop_application_kyc_review_and_owner_notification`, `Kyc_documents_have_no_public_url_and_the_bucket_refuses_anonymous_reads`, `Shop_names_are_unique_and_rejection_needs_a_reason` |
| Cột nhạy cảm mã hoá | `AesGcmDataEncryptor` (số TK, CCCD), chỉ hiện 4 số cuối | `Shop_application_kyc_review…` kiểm `v1:` và che số |
| Kênh Người Bán phần sản phẩm | `seller/`: danh sách theo tab (Tất cả / Đang hoạt động / Hết hàng / Sắp hết hàng / Chờ duyệt / Vi phạm / Đã ẩn / Nháp), sửa nhanh giá/tồn, nhập–xuất kho + lịch sử, trình soạn (danh mục + gợi ý, thuộc tính ngành, ≤ 9 ảnh + video, dựng phân loại 2 tầng → bảng SKU, áp dụng hàng loạt), thiết lập shop, tạm nghỉ | e2e `seller.spec.cjs` (đăng ký shop → duyệt → đăng sản phẩm 2 tầng qua UI → quản trị duyệt qua UI) |
| Quản trị duyệt sản phẩm | `GET /api/admin/products` (cũ nhất trước, cờ từ khoá cấm), duyệt / yêu cầu sửa / khoá / mở khoá; trang **Duyệt sản phẩm** | `Banned_keywords_flag_the_product_for_review`, e2e trên |
| IDOR shop | `SellerAccess` lọc `shop_staff` trong SQL | `Other_shops_products_are_404_and_missing_staff_permission_is_403`, `Someone_elses_upload_cannot_be_attached` |
| Gieo 40 sản phẩm cũ thành dữ liệu thật | `CatalogSeeder` + `catalog-seed.json` (18 ngành, 87 danh mục lá, 30 shop gồm 6 Mall, 40 sản phẩm, 151 SKU, 107 ảnh) | stack: `catalog.products` 40 dòng `Active`, ảnh `GET /s3/…` → 200 `image/webp` |
| Ảnh đại diện | `PUT /api/account/avatar` (ảnh ≤ 1 MB, cỡ 600) | `Avatar_is_limited_to_one_megabyte` |
| **Chưa làm** (xem 00 #41) | Excel hàng loạt, tài khoản phụ & trang trí shop, UI ảnh đại diện ở `web` | — |

## Phase 4 — Tìm kiếm & trang người mua

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Tìm không dấu, chịu lỗi gõ, đồng nghĩa | Meilisearch, trường bỏ dấu, `SEARCH.SYNONYMS` (00 #42) | `Accent_insensitive_typo_tolerant_and_synonyms`; e2e `Tìm "dien thoai" (không dấu) ra "Điện Thoại…"` |
| Facet: danh mục, nơi bán, thương hiệu, đánh giá, loại shop, tình trạng, thuộc tính; lọc giá | `MeiliProductSearch` / `PostgresProductSearch`, `FacetLabeler` | `Facet_counts_match_an_independent_sql_count` (đếm bằng SQL độc lập); e2e `Số trên facet khớp số kết quả khi bấm lọc` |
| Sắp xếp liên quan / mới / bán chạy / giá; phân trang ổn định 60/trang | `sort` + `id` làm khoá cuối | e2e `Sắp xếp theo giá tăng dần…`, `…vẫn đúng khi có từ khoá` |
| Dự phòng khi công cụ tìm kiếm ngừng | `ResilientProductSearch` (00 #43) | `Postgres_fallback_returns_the_same_results_and_counts_as_meilisearch` (kể cả từ khoá chỉ có ở tên ngành) |
| Đồng bộ chỉ mục | outbox `search.sync.*` (00 #44) | mọi phép thử Phase 4 tạo dữ liệu qua EF rồi tìm thấy sau khi chạy outbox; `Hidden_or_sold_out_products_leave_the_results` |
| Gợi ý khi gõ + từ khoá hot | `SuggestQuery`, `HotKeywordsQuery` (`engage.search_logs`, từ khoá bỏ dấu) | `Hot_keywords_and_suggestions_come_from_real_searches`; e2e `Gợi ý tìm kiếm từ máy chủ…` |
| Trang sản phẩm: breadcrumb, ảnh/video, 2 tầng phân loại, lựa chọn hết hàng bị mờ, giá theo SKU, shop, sản phẩm liên quan / cùng shop, shop tạm nghỉ không mua được | `GetProductPage`, `ProductDetail.tsx` | `Product_page_marks_sold_out_options_and_vacation_shops_cannot_sell`; e2e `Bắt buộc chọn phân loại; phân loại hết hàng bị mờ` |
| Lượt xem chống trùng, "đã xem gần đây" | `RecordProductView` + advisory lock, cookie `sh_vid` | `Views_are_counted_once_per_viewer_within_the_window` (song song) |
| Yêu thích, theo dõi shop, bộ đếm | `EngageController`, tính lại từ nguồn (00 #45) | `Parallel_likes_and_follows_are_counted_once_and_recomputed`, `A_seller_cannot_follow_their_own_shop`; e2e yêu thích & theo dõi |
| Trang chủ: danh mục, Mall, tìm kiếm hàng đầu, gợi ý hôm nay (cá nhân hoá theo đã xem), đã xem | `/api/home/*`, `HomePage.tsx` | `Recommendations_put_the_viewers_interests_first`; e2e `Các section trang chủ lấy dữ liệu thật từ API` |
| Trang danh mục, trang shop | `/danh-muc/{slug}`, `/shop/{slug}` | e2e `Click danh mục mở trang danh mục có breadcrumb`, `Trang Shop: theo dõi tăng số người theo dõi` |
| ~1.000 sản phẩm mẫu | `ProductGenerator` (tất định, qua domain) | stack: 999 sản phẩm `Active` được lập chỉ mục khi khởi động |
| Bỏ dữ liệu giả ở `web` | xoá `data/products.ts`, `data/images.ts`; giỏ hàng theo SKU | `tsc -b`, `vitest` (quy tắc màu / ngày giờ / đường dẫn API), 23 e2e người mua |
| **Dời lại** | Flash Sale (Phase 9), giỏ & thanh toán thật (Phase 5), đánh giá (Phase 7) | 00 #47 |

## Phase 5 — Giỏ hàng & thanh toán

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Giỏ theo SKU, nhóm theo shop, chọn shop / tất cả, đổi phân loại tại chỗ, báo đổi giá / hết hàng từng dòng | `sales.carts/cart_items`, `CartStore`, `CartPage.tsx` | `Cart_reports_price_changes_and_shortages_per_line_instead_of_dropping_them`; e2e `Giỏ hàng: checkbox…`, `Cập nhật số lượng…` |
| Gộp giỏ khách khi đăng nhập | cookie `sh_cart`, `MergeGuestCartCommand` trong `/api/auth/*` (00 #48) | `Guest_cart_is_merged_into_the_account_at_sign_in`; e2e **số 2** `Khách thêm giỏ → đăng nhập → giỏ được gộp` |
| PricingEngine, phân bổ dư lớn nhất xuống từng dòng | `PricingEngine` (00 #49), `order_item_discounts` | 12 phép thử đơn vị `PricingEngineTests`; `Two_shop_checkout_with_every_discount_creates_two_orders_that_add_up_exactly` |
| Voucher sàn & shop, miễn ship, hoàn xu; hiện cả mã không dùng được kèm lý do | `VoucherEvaluator`, `CheckoutBuilder`, trang Thanh toán | `Unusable_vouchers_are_listed_with_the_reason` ("Mua thêm ₫35.000…") |
| Xu: dùng ≤ X% khi thanh toán, hoàn khi huỷ | `promo.coin_ledger` (số dư = tổng), `CoinWallet` | `Failed_then_abandoned_payment_gives_back_stock_voucher_and_coins_when_it_expires` |
| Vận chuyển giả lập, cân quy đổi, ngày dự kiến | `SimulatedCarrier`, `ShippingCalculator` (00 #54) | `ShippingRuleTests`, `Express_shipping_is_only_offered_within_the_same_province` |
| Checkout tách đơn theo shop, idempotency | `PlaceOrderHandler` (00 #50) | `The_same_idempotency_key_in_parallel_creates_one_checkout` (10 yêu cầu song song) |
| Giữ kho không bán âm | UPDATE có điều kiện + CHECK | `Fifty_parallel_buyers_for_ten_units_get_exactly_ten_orders` (50 song song / 10 chiếc → đúng 10) |
| Voucher không vượt quota / lượt mỗi người | UPDATE có điều kiện, `voucher_user_counters`, CHECK `ck_vouchers_quota` | `Voucher_quota_and_per_user_limit_hold_under_parallel_checkouts` |
| Giá đổi giữa báo giá và đặt hàng | so `expectedGrandTotal` → 409 kèm báo giá mới | `A_price_change_between_quote_and_order_is_a_409_with_the_new_quote` |
| COD + SimulatedGateway, webhook đúng một lần | `PaymentProcessor`, `SimulatedGateway` (00 #51, #52) | `Paying_on_the_simulated_gateway_…`, `A_webhook_is_applied_exactly_once_even_when_delivered_in_parallel`, `A_forged_or_wrong_amount_webhook_changes_nothing`, `Money_arriving_after_expiry_is_refunded…` |
| Nhả kho quá hạn, đối chiếu giao dịch treo | `PaymentExpiryService` (00 #53) | `A_lost_callback_is_found_by_asking_the_gateway_before_expiring`; e2e **số 4** `Bỏ dở thanh toán online → quá hạn → huỷ đơn và nhả kho`, `Thanh toán online…: thành công / thất bại (thanh toán lại)` |
| e2e số 3: 2 shop + voucher shop + voucher sàn + xu → COD → 2 đơn, tổng khớp từng đồng | trang Thanh toán, `/dat-hang-thanh-cong` | e2e `Giỏ 2 shop + voucher shop + voucher sàn + xu → COD → 2 đơn, tổng khớp từng đồng` (so cả từng khoản giảm qua API) |
| Đơn mua (danh sách theo tab, chi tiết, lịch sử) | `/api/orders`, `/tai-khoan/don-mua` | `Another_buyers_order_and_checkout_are_not_found` (IDOR → 404) |
| Ví voucher, ShopHub Xu, voucher của shop (Kênh Người Bán), voucher sàn + cộng xu (quản trị) | `/tai-khoan/voucher`, `/tai-khoan/xu`, seller `Mã giảm giá`, admin `Voucher của sàn`, nút "Xu" ở Người dùng | kiểm thủ công trên stack; API có phép thử |
| Máy trạng thái đơn (một lớp duy nhất) | `OrderStateMachine` | `OrderStateMachineTests`; quy tắc quét mã `Order_status_is_only_assigned_inside_OrderStateMachine` |
| **Dời lại** | đa kho, giới hạn mua/người theo sản phẩm, combo / Flash Sale trong giá (Phase 9), hoàn xu từ voucher & hết hạn xu (Phase 6/7), Ví ShopHub (Phase 8) | 00 #56 |

## Phase 6 — Đơn hàng & vận chuyển

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Máy trạng thái đầy đủ (chờ xác nhận → chờ lấy hàng → đang giao → đã giao → hoàn thành; giao thất bại → đang hoàn → đã hoàn; huỷ) | `OrderStateMachine`, `ShipmentEventProcessor` | `Cod_order_goes_from_confirmation_to_completed_through_the_simulated_carrier`, `A_parcel_that_cannot_be_delivered_comes_back_into_stock_and_the_money_goes_back` |
| Trừ kho khi giao cho hãng, hoàn kho khi hoàn về, "đã bán" tính lại | 00 #58 | cùng hai phép thử trên (tồn 10 → 8; hoàn về → 10; `SoldCount` = 2) |
| Xử lý đơn phía shop: tab, lọc, tìm, chuẩn bị hàng (khung giờ / bưu cục), chọn nhiều, in phiếu giao (PDF A6/A5, mã vạch) & phiếu soạn hàng, xuất Excel, ghi chú nội bộ | `SellerOrderFeatures`, `ShippingDocuments`, seller `Đơn hàng` | PDF đọc lại được chữ có dấu (PdfPig), `Picking_list_and_excel_export_are_real_files`; e2e **số 5** (in phiếu, tệp `%PDF-` hợp lệ) |
| Bảng điều khiển shop | `SellerDashboardQuery`, seller `Bảng điều khiển` | e2e số 6 (ô "Yêu cầu huỷ" = 1) |
| Hành trình vận đơn, tra cứu công khai | `shipments/shipment_events`, `/tra-cuu-van-don` | e2e số 5 (5 mốc, không lộ tên người nhận) |
| Hãng giả lập đẩy trạng thái qua webhook có chữ ký, đúng một lần | 00 #59 | `Carrier_webhooks_need_the_signature_and_replays_change_nothing` |
| Huỷ trước xác nhận / yêu cầu huỷ sau xác nhận / shop từ chối / tự chấp thuận | `CancelMyOrder`, `RequestCancel`, `DecideCancelRequest`, `OrderAutomationService` | `Buyer_cancels_before_confirmation_…`, `After_confirmation_the_buyer_must_ask_and_the_shop_may_refuse`, `An_unanswered_cancel_request_is_approved_automatically…`; e2e **số 6** |
| Huỷ đã thanh toán online → hoàn tiền qua cổng | `OrderCanceller.RefundIfPaidAsync`, `sales.refunds` | `A_seller_cancelling_a_paid_order_refunds_it_to_the_gateway` |
| Người mua huỷ và shop xác nhận cùng lúc → một bên thắng | khoá theo đơn (00 #60) | `Buyer_cancel_racing_the_seller_confirm_has_exactly_one_winner` (3 vòng song song) |
| Tự hoàn thành sau N ngày; tự huỷ đơn shop chậm + điểm phạt | 00 #61 | `Delivered_orders_complete_themselves_and_late_shops_are_penalised_once` |
| "Đã nhận được hàng", "Mua lại" | `ConfirmReceived`, `BuyAgain` | e2e số 5 |
| Thông báo theo sự kiện | 00 #63, `/thong-bao`, chuông header | phép thử vòng đời kiểm 5 thông báo của người mua |
| IDOR đơn / sản phẩm | lọc chủ sở hữu trong SQL | `Shops_and_buyers_only_reach_their_own_orders`; e2e **số 11** |
| Thân dữ liệu thiếu trường không gây 500 | 00 #65 | `MalformedInputTests` |
| **Dời lại** | đánh giá, trả hàng (Phase 7), giải ngân (Phase 8), đẩy tức thời (Phase 10) | 00 #67 |

## Phase 7 — Đánh giá, trả hàng, khiếu nại

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Đánh giá sau khi hoàn thành: sao, thẻ, nội dung, ảnh/video, ẩn danh, sửa 1 lần, thưởng xu | 00 #68, `ReviewFeatures`, web `/tai-khoan/don-mua/{mã}/danh-gia` | `A_completed_order_can_be_reviewed_once_with_a_coin_reward_and_ratings_follow`; e2e **số 5** (đánh giá kèm ảnh → điểm sản phẩm 4.0, có xu) |
| Trang sản phẩm: tóm tắt sao, lọc (sao / có ảnh / có bình luận), phản hồi của shop, báo cáo | `ProductReviewsQuery`, `components/ProductReviews.tsx` | phép thử trên; e2e số 5 |
| Shop trả lời đánh giá; quản trị ẩn đánh giá bị báo cáo | `ReplyReview`, `ResolveReviewReport`; seller `Đánh giá`, admin `Báo cáo đánh giá` | `Only_completed_orders_can_be_reviewed_and_reported_reviews_can_be_hidden` |
| Trả hàng một phần, hoàn đúng phần đã trả sau giảm giá | 00 #69, `ReturnPricing` | `Successive_partial_returns_add_up_to_exactly_what_was_paid_for_the_line`, `Partial_returns_refund_exactly_what_was_paid_after_every_discount`; e2e **số 7** (2 chiếc, SHOPHUB50 → hoàn 1 chiếc đúng ₫134.000) |
| Shop chấp nhận / từ chối / đề nghị một phần / xác nhận đã nhận hàng (nhập lại kho) | `ShopReturnAction`, seller `Trả hàng / Hoàn tiền` | `Return_and_refund_ships_the_goods_back_then_the_shop_checks_and_restocks` |
| Khiếu nại → sàn phân xử | `DecideDispute`, admin `Khiếu nại trả hàng` | `Rejected_return_goes_to_a_dispute_and_the_admin_decides_for_the_buyer`; e2e **số 7** |
| Hạn xử lý tự động | 00 #70, `ReturnAutomationService` (chạy trong `sales.order-automation`) | `Deadlines_auto_approve_unanswered_returns_and_close_undisputed_rejections_and_late_requests_are_refused` |
| Không trả trùng khi gửi song song | index `ux_return_items_open` | `Only_one_open_return_per_line_even_in_parallel` |
| Hoàn tiền thu hồi thưởng đánh giá; IDOR trả hàng | 00 #68 | `A_refunded_line_loses_its_review_reward_and_strangers_cannot_touch_returns` |
| **Dời lại** | ghi có ví cho hoàn COD và khoá giải ngân (Phase 8), chat trong yêu cầu (Phase 10) | 00 #71, #73 |

## Phase 8 — Tài chính

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Sổ cái kép, số dư = tổng bút toán, cột chép sẵn có phép đo đối chiếu | 00 #74, `Ledger`, `LedgerCheckService` (`finance.ledger-check`), quản trị → Tài chính → Sổ cái | mọi phép thử Phase 8 kết thúc bằng `CheckLedger`: 0 tài khoản lệch, 0 bút toán không cân, Σ Nợ = Σ Có |
| Công thức doanh thu shop (3.9), voucher sàn / xu là trợ giá của sàn | 00 #76, `SettlementCalculator` | `SettlementCalculatorTests` (5); `A_completed_order_waits_for_release_with_exact_earnings…` |
| Biểu phí theo ngành, có hiệu lực từ ngày | 00 #77, `FeeSchedule`, quản trị → Biểu phí | phép thử trên (phí 5% riêng cho ngành lá), `…fees_cannot_be_backdated` |
| Chờ giải ngân → giải ngân, khoá giải ngân khi đang trả hàng | 00 #78, `SettlementService`, Kênh Người Bán → Tài chính | `An_open_return_holds_the_release_and_a_cod_refund_goes_to_the_buyers_wallet…`; e2e **số 5** (chờ giải ngân → giải ngân → rút tiền) |
| Hoàn tiền về đúng nguồn: cổng / Ví ShopHub / xu | `OrderLedger`, `ReturnRefunder`, `OrderCanceller` | phép thử trên (COD → ví), `Wallet_is_topped_up_once_pays_orders_with_the_pin_and_takes_refunds_back` (huỷ đơn trả bằng ví → về ví) |
| Rút tiền: tài khoản đã xác minh (OTP), tối thiểu, số lần / tuần, sàn duyệt hoặc tự động, **không vượt số dư khi song song** | 00 #79, `WithdrawalService` | `Parallel_withdrawals_never_take_more_than_the_available_balance` (8 song song → 3), `Withdrawals_need_a_verified_account_a_minimum_and_a_weekly_limit…` |
| Ví ShopHub: nạp qua cổng (đúng một lần), trả đơn bằng mật khẩu 6 số, nhận hoàn, rút | 00 #80, web `/tai-khoan/vi`, lựa chọn "Ví ShopHub" ở trang thanh toán | phép thử ví ở trên; `Parallel_wallet_checkouts_cannot_spend_more_than_the_balance` (5 song song → 1) |
| Báo cáo đối soát theo kỳ (Excel + PDF) khớp từng đồng, hoá đơn phí sàn | 00 #81, `FinanceDocuments` | phép thử giải ngân đọc lại Excel (ClosedXML) và PDF (PdfPig): dòng đơn và dòng tổng = số đã giải ngân |
| Đối soát với cổng thanh toán và hãng vận chuyển: khớp từng giao dịch, liệt kê lệch | 00 #81, quản trị → Đối soát | `Reconciliation_matches_every_transaction_and_lists_each_difference` |
| IDOR tài chính | lọc chủ sở hữu trong SQL | `Finance_of_another_shop_or_another_buyer_is_not_found…` |
| **Dời lại** | chương trình Freeship / Voucher Xtra (Phase 9), cổng / hãng thật (Phase 11), báo cáo toàn sàn (Phase 12) | 00 #82 |

## Phase 9 — Marketing

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Một SKU chỉ ở một chương trình giá tại một thời điểm (ràng buộc CSDL) | 00 #83, exclusion constraint `ex_price_programs_sku_period` | `A_discount_price_reaches_cart_checkout_and_order_and_a_sku_cannot_be_in_two_price_programmes_at_once` |
| Chương trình giảm giá theo SKU: giá tới giỏ, báo giá, đơn | `PriceBook`, `CartStore`, `CheckoutBuilder`, `OrderItem.PriceSource` | phép thử trên |
| Combo, mua kèm deal sốc, quà tặng; combo do shop chịu | 00 #85, `DealsBook`, `PricingEngine` (bước 0) | `Combo_add_on_and_gift_change_the_checkout_and_the_combo_is_the_shops_cost`, `MarketingRulesTests` (3 phép thử combo) |
| Flash Sale của sàn: khung giờ cấu hình, tiêu chí, đăng ký, duyệt | 00 #84, quản trị → Marketing, Kênh Người Bán → Kênh Marketing | `Platform_flash_registration_checks_the_criteria_and_an_approved_item_shows_on_the_board_with_server_time` |
| Suất Flash Sale: Redis Lua + đối chiếu PostgreSQL, giới hạn mỗi người, không vượt suất khi song song | `RedisFlashSaleCounter`, `FlashSaleQuota`, `FlashSaleReconciler` | `Thirty_buyers_at_once_get_exactly_the_quota_and_a_cancelled_order_gives_its_unit_back`, `One_buyer_cannot_take_more_flash_units_than_the_limit…`; **e2e số 8** (30 phiên trình duyệt → đúng 10 đơn) |
| Chịu 1.000 người đồng thời lúc mở Flash Sale | `e2e/load/flash-sale.js` (k6) | 1.000 người tranh 44 suất → đúng 44 đơn, 956 bị từ chối đúng (409), 0 lỗi máy chủ, p95 đặt hàng 2,13 s trên một container API ở máy dev (ngưỡng 3 s) |
| Thanh "Đã bán" thật, đếm ngược theo giờ máy chủ | `FlashBoardQuery` (`serverTime`), `Countdown` | e2e trang chủ (khối Flash Sale + đồng hồ), phép thử bảng Flash Sale |
| Banner, lối tắt, popup (tần suất), chiến dịch / trang sự kiện | 00 #86, `/su-kien/:slug` | `Banners_and_campaign_pages_come_from_the_admin_and_only_marketing_admins_edit_them`; e2e popup |
| Hạng thành viên & voucher theo hạng, điểm danh 7 ngày, hoàn xu từ voucher, xu hết hạn | 00 #87 | `Spending_unlocks_the_gold_tier_voucher_and_voucher_cashback_is_paid_once…`, `Check_in_pays_the_streak_reward_once_per_day…`, `MarketingRulesTests.Spending_uses_the_xu_that_expire_first…` |
| Dữ liệu mẫu: 1 khung đang chạy + 1 sắp tới, banner, 1 chiến dịch | `MarketingSeeder` | stack dev: log `SEED flash sale / banners / campaign` |
| **Dời lại** | thông báo hàng loạt (Phase 10), Freeship / Voucher Xtra, giá khuyến mãi trên thẻ lưới | 00 #89 |

## Phase 10 — Chat & thông báo

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| Chat người mua ↔ shop thời gian thực (WebSocket, nhiều bản API) | 00 #90–91, hub `/hubs/realtime` + Redis backplane, web: cửa sổ chat nổi + `/chat`, Kênh Người Bán → Chat | `Buyer_and_shop_talk_live_with_read_receipts_typing_and_unread_counters` (client SignalR thật); **e2e số 10** (hai phiên trình duyệt qua gateway) |
| Đã xem, đang soạn tin, số chưa đọc | `chat.read`, `chat.typing`, cột chưa đọc mỗi phía | hai phép thử trên |
| Gửi ảnh, thẻ sản phẩm / đơn / voucher; nút "Chat ngay" ở trang sản phẩm và trang shop | `ChatViews`, `ChatNowButton` | `Contact_details_are_flagged_not_blocked_and_cards_only_show_what_the_sender_may_share`; e2e số 10 (thẻ sản phẩm) |
| Cảnh báo thông tin liên hệ ngoài sàn | 00 #92, `ContactFilter` | phép thử trên; e2e số 10 (cảnh báo ở cả hai phía) |
| Chặn / báo cáo shop | chặn / báo cáo trên hội thoại | `Outside_working_hours_the_buyer_gets_the_automatic_reply_once_an_hour_and_a_blocked_shop_cannot_write` |
| Hộp thư shop: lọc, phân công, câu trả lời nhanh, tự động trả lời ngoài giờ | 00 #93 | phép thử trên; e2e số 10 (câu trả lời nhanh qua `/`) |
| Tỉ lệ / thời gian phản hồi của shop | `ChatPerformanceService`, trang sản phẩm + trang shop | phép thử realtime ở trên |
| Giới hạn tốc độ chat | `ChatRateLimit` 60 / phút / người | `A_new_message_notifies_the_shop_live_and_by_email_when_chosen_and_messages_are_rate_limited` |
| Thông báo đa kênh theo lựa chọn của người dùng | 00 #94, `/tai-khoan/thong-bao`, `NotificationDeliveryHandler` | phép thử trên (email chỉ khi bật); chuông thông báo cập nhật realtime |
| Thông báo hàng loạt theo phân khúc, ≤ 1 khuyến mãi / người / ngày | 00 #95, quản trị → Marketing → Thông báo đẩy | `A_broadcast_reaches_its_segment_once_and_nobody_gets_two_promotions_the_same_day` |
| Nhắc việc: đơn sắp tự hoàn thành, voucher sắp hết hạn, yêu thích giảm giá | `ReminderService` (`engage.reminders`) | `Reminders_tell_about_orders_completing_soon_once` |
| **Dời lại** | push FCM thật, SMS thật (Phase 11), quản trị xem chat bị báo cáo (Phase 12) | 00 #96 |

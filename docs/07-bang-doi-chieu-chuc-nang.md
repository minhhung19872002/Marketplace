# 07 — Bảng đối chiếu chức năng

Từng mục của đặc tả → nơi hiện thực → bằng chứng. Cập nhật sau mỗi phase.

## Tiến độ theo phase (mục 11)

| Phase | Nội dung | Trạng thái | Bằng chứng |
|---|---|---|---|
| 0 | Chuyển đổi repo | **Xong** | Xem bảng Phase 0 dưới đây |
| 1 | Nền móng backend | **Xong** | Xem bảng Phase 1 dưới đây |
| 2 | Tài khoản | **Xong** | Xem bảng Phase 2 dưới đây |
| 3 | Danh mục & sản phẩm | **Xong** | Xem bảng Phase 3 dưới đây |
| 4 | Tìm kiếm & trang người mua | **Xong** | Xem bảng Phase 4 dưới đây |
| 5 | Giỏ hàng & thanh toán | **Xong** | Xem bảng Phase 5 dưới đây |
| 6 | Đơn hàng & vận chuyển | **Xong** | Xem bảng Phase 6 dưới đây |
| 7 | Đánh giá, trả hàng, khiếu nại | **Xong** | Xem bảng Phase 7 dưới đây |
| 8 | Tài chính | **Xong** | Xem bảng Phase 8 dưới đây |
| 9 | Marketing | **Xong** | Xem bảng Phase 9 dưới đây |
| 10 | Chat & thông báo thời gian thực | **Xong** | Xem bảng Phase 10 dưới đây |
| 11 | Cổng thật | **Xong** (chưa chạy với sandbox thật — cần tài khoản thử, xem cuối bảng) | Xem bảng Phase 11 dưới đây |
| 12 | Quản trị & báo cáo | **Xong** | Xem bảng Phase 12 dưới đây |
| 13 | Hoàn thiện | **Xong** (cổng / hãng thật chờ tài khoản sandbox) | Xem bảng Phase 13 dưới đây |

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
| Đã bổ sung (00 #30) | Ảnh đại diện (Phase 13, 00 #127), tài khoản ngân hàng nhận tiền (Ví ShopHub, Phase 8 — không lưu thẻ: hoàn tiền về nguồn hoặc về ví, rút về tài khoản ngân hàng), cài đặt thông báo (Phase 10), Google (Phase 13, 00 #134) | xem bảng Phase 13 |

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
| Đã bổ sung (00 #41) | Excel hàng loạt, tài khoản phụ, trang trí & danh mục shop, ảnh đại diện — Phase 13 (00 #120–122, #127, #130) | xem bảng Phase 13 |

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
| Đã làm ở phase sau | Flash Sale (Phase 9), giỏ & thanh toán (Phase 5), đánh giá (Phase 7) | 00 #47 |

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
| Đã làm ở phase sau | giới hạn mua mỗi người (Phase 13, 00 #128), giá combo / Flash Sale (Phase 9), hoàn xu & hết hạn xu (Phase 9), Ví ShopHub (Phase 8); đa kho (Phase 13, 00 #139) | 00 #56 |

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
| Đã làm ở phase sau | đánh giá, trả hàng (Phase 7), giải ngân (Phase 8), đẩy tức thời (Phase 10) | 00 #67 |

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
| Đã làm ở phase sau | ghi có ví cho hoàn COD, khoá giải ngân (Phase 8); chat từ yêu cầu trả hàng ("Liên hệ shop", Phase 13, L052) | 00 #71, #73 |

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
| Đã làm ở phase sau | Freeship / Voucher Xtra (Phase 13, 00 #132), cổng / hãng thật (Phase 11), báo cáo toàn sàn (Phase 12) | 00 #82 |

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
| Đã làm ở phase sau | thông báo hàng loạt (Phase 10), Freeship / Voucher Xtra (Phase 13), giá khuyến mãi trên thẻ (Phase 13, L048) | 00 #89 |

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
| Theo đặc tả / đã làm | SMS: interface + bản giả lập ghi bảng (đúng mục VII); push FCM: chuẩn bị sẵn (`device_tokens`, kênh push ghi log) — gửi thật cần khoá của nhà cung cấp; chat bị báo cáo (Phase 13, 00 #124) | 00 #96 |

## Phase 11 — Cổng thanh toán & hãng vận chuyển thật

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| VNPay sandbox: tạo URL ký, IPN đúng một lần, mã trả lời đúng khuôn, hoàn tiền | 00 #98, `VnPayGateway` | `Vnpay_pays_through_a_signed_url_and_ipn_once_and_refunds_on_cancel` (sai chữ ký → 97, sai số tiền → 04, lặp → 02, huỷ đơn → refund 02); `ProviderRulesTests` (chuỗi ký, IPN, mã trả lời) |
| Đối chiếu giao dịch treo bằng cách hỏi lại cổng | `querydr` / MoMo `query` trong `PaymentExpiryService` | `A_vnpay_payment_whose_ipn_never_came_is_found_by_querying_vnpay_before_the_order_expires` |
| MoMo sandbox: tạo giao dịch, IPN ký HMAC-SHA256 (204), nạp Ví ShopHub | 00 #99, `MoMoGateway`, chọn cổng khi nạp ví | `A_momo_wallet_topup_is_credited_once_from_the_signed_ipn`; `ProviderRulesTests.Momo_ipn_signature_…` |
| GHN sandbox: phí, tạo đơn với mã quận / phường của GHN, huỷ, webhook, đồng bộ bù | 00 #100–101, `GhnCarrier`, `CarrierSyncService` | `Ghn_quotes_books_with_its_own_district_ids_and_follows_webhooks_and_missed_events` |
| GHTK sandbox: phí theo tên địa danh, tạo đơn (lấy tận nơi / gửi bưu cục), phiếu PDF, webhook form | 00 #100–102, `GhtkCarrier`, nút "Phiếu GHTK" | `Ghtk_books_by_place_names_serves_its_own_label_and_takes_form_webhooks` |
| Không có khoá thì giả lập vẫn chạy; hãng lỗi không chặn thanh toán | 00 #97, #102 | toàn bộ bộ test cũ (kênh thật tắt) + `A_carrier_outage_drops_only_that_option_and_checkout_goes_on`; e2e trên stack không có khoá |
| Ánh xạ trạng thái hãng → trạng thái vận đơn, chuẩn hoá tên địa danh | `GhnCarrier.Map`, `GhtkCarrier.Map`, `DivisionNameResolver` | `ProviderRulesTests` (9 + 8 + 8 trường hợp) |
| **Chưa kiểm với sandbox thật** | cần tài khoản thử của từng nhà cung cấp | 00 #103, `docs/04` |

## Phase 12 — Quản trị & báo cáo

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| VI.1 Tổng quan: GMV, đơn, người mua / shop mới, tỉ lệ huỷ / hoàn, doanh thu phí, theo ngày / tuần / tháng, biểu đồ, việc chờ xử lý | `AdminOverviewHandler`, admin → Tổng quan (Recharts) | e2e `reports.spec.cjs` (KPI có số); phép thử GMV ở dưới |
| VI.10 Báo cáo: GMV theo thời gian / ngành / tỉnh, top shop, top sản phẩm, voucher, Flash Sale, huỷ theo shop & lý do, trả hàng, người dùng mới & quay lại, phễu — bảng + biểu đồ + Excel / PDF, **khớp truy vấn độc lập** | 00 #104–106, `AdminReports`, admin → Báo cáo | `Gmv_reports_tie_out_with_independent_sql_and_the_excel_and_pdf_carry_the_same_numbers` (SQL viết riêng, đọc lại Excel bằng ClosedXML, PDF bằng PdfPig), `Funnel_and_seller_analytics_follow_views_cart_adds_and_orders` |
| Khoảng ngày ngược báo lỗi ở một chỗ | `ReportRange.Of` | phép thử GMV ở trên (409 tiếng Việt) |
| III.8 Dữ liệu & phân tích của shop (kỳ trước, top sản phẩm, nguồn truy cập, xuất Excel) + hiệu quả hoạt động (đơn không thành công, giao trễ, phản hồi chat, điểm phạt & hậu quả) | `SellerAnalytics`, Kênh Người Bán → Dữ liệu & phân tích | phép thử phễu ở trên (doanh số, chuyển đổi 100%, nguồn Search, IDOR → 404); e2e `reports.spec.cjs` |
| VI.2 Người dùng: chi tiết (đơn, đánh giá, vi phạm, thiết bị), đặt lại mật khẩu cắt phiên, nhật ký theo người | 00 #109, `AdminUserDetailHandler`, `AdminResetPasswordHandler` | `Cms_help_templates_password_reset_and_gateway_switch_work_end_to_end` (mật khẩu cũ hỏng, mật khẩu tạm phải đổi, phiên cũ 401) |
| VI.3 Điểm phạt: luật cộng điểm, ngưỡng & hậu quả, gỡ điểm, lịch sử | 00 #107, `ShopPenaltyService`, admin → Shop → Điểm phạt | `Penalty_points_are_recomputed_from_their_rows_and_each_threshold_has_its_consequence` (hạn chế hiển thị, cấm đăng ký Flash Sale 409, điểm hết hạn không tính, khoá shop, gỡ điểm) |
| VI.4 Cây danh mục kéo thả, thương hiệu, báo cáo sản phẩm vi phạm, khoá hàng loạt (có trần) | `MoveCategoryHandler`, `BulkBanProductsHandler`, `ProductReport*` | `A_category_moves_with_its_subtree_but_never_deeper_than_three_levels`, `Buyer_reports_a_product_once_and_banning_it_closes_every_open_report` (trùng → 409, 101 sản phẩm → 400) |
| VI.5 Tra đơn toàn bộ lịch sử, can thiệp có kiểm soát (quyền riêng, lý do, nhật ký) | 00 #108, admin → Đơn hàng | `Admin_cancels_an_order_with_a_reason_and_a_failed_refund_can_be_sent_to_the_wallet` (không quyền 403, lý do ngắn 400, lệnh hoàn lỗi → ví, xử lý lần hai 409); e2e `reports.spec.cjs` |
| VI.8 Trang tĩnh, trợ giúp, mẫu thư / SMS, tham số có lịch sử, bật / tắt đơn vị vận chuyển & cổng thanh toán | 00 #110, admin → Nội dung & mẫu tin, Vận chuyển & cổng thanh toán | phép thử nội dung ở trên (HTML bị lọc `<script>`, mẫu OTP mới tới SMS, biến lạ → 409, tắt MoMo biến mất khỏi nạp ví); e2e trang pháp lý / trợ giúp / footer |
| VI.9 Nhật ký: lọc người / hành động / đối tượng / thời gian, khác biệt cũ / mới, xuất Excel | `AuditLogsExportHandler`, admin → Nhật ký | phép thử nội dung ở trên (xuất có dòng đổi tham số) |
| Đã làm ở Phase 13 | trang trí shop (00 #121), Excel hàng loạt (00 #130), mẫu thông báo từng sự kiện (00 #125) | 00 #111 |

## Phase 13 — Hoàn thiện

| Yêu cầu | Hiện thực | Bằng chứng |
|---|---|---|
| 6.6 SEO: HTML render sẵn cho máy thu thập — title, meta, Open Graph, canonical, JSON-LD Product/Offer/AggregateRating, breadcrumb | 00 #114, `SeoRenderQuery`, `map $sh_is_bot` ở gateway | `A_product_page_for_crawlers_has_meta_open_graph_canonical_and_product_json_ld`, `Category_shop_search_and_static_pages_render_and_search_is_not_indexed`; e2e `seo.spec.cjs` (qua gateway, người thật vẫn nhận SPA) |
| 6.6 `sitemap.xml` chia tệp ≤ 50.000 URL, `robots.txt`, URL thân thiện | 00 #115, `SitemapHandlers`, `lib/urls.ts` | `The_sitemap_index_lists_chunked_files_holding_canonical_urls_and_robots_points_to_it`; e2e `seo.spec.cjs`; e2e "Click sản phẩm mở trang chi tiết" (URL chuẩn) |
| 6.3 Tìm kiếm < 500 ms, trang < 300 ms với 1 triệu sản phẩm | 00 #116–118, `PerfSeeder`, `docker-compose.perf.yml`, `e2e/load/perf.js` | docs/04 mục Hiệu năng: p95 không cache 131 / 110 / 102 / 62 / 58 ms trên 1.001.000 sản phẩm, 0 lỗi |
| 6.1 Rà bảo mật: IDOR, giới hạn tốc độ, tải tệp, tiêu đề bảo mật (thêm HSTS) | quét mã `EndpointAuthorisationTests`, `NginxConfigParityTests` (thêm `Strict-Transport-Security`) | e2e số 11 (IDOR), số 12 (khoá phiên — `hardening.spec.cjs`); bộ test tích hợp tải tệp / giới hạn tốc độ của Phase 1–3 |
| 6.5 / 9 #13: 375 px không cuộn ngang, seller/admin 1366 × 768 | 00 #113 | e2e `hardening.spec.cjs` (mọi trang người mua, 1366 px cho hai gói kia); L041–L043 |
| 6.5 WCAG AA: tương phản, bàn phím | 00 #112, `--sh-focus`, `theme.ts` | `contrast.test.ts` ×3; e2e `a11y.spec.cjs` (axe, Tab tới ô tìm kiếm); L044 |
| 7 `docker-compose.prod.yml`: HTTPS, giới hạn tài nguyên, restart, log driver; triển khai tự dọn ảnh cũ | 00 #119, `gateway-https.conf`, `deploy/scripts/deploy.sh` | `The_https_gateway_routes_exactly_like_the_dev_gateway`; `docker compose -f docker-compose.yml -f docker-compose.prod.yml config` hợp lệ |
| 6.4 Sao lưu / phục hồi | `backup-db`, `backup-files`, `deploy/scripts/restore.sh` | Diễn tập trên CSDL dev: 489 đơn, 1.096 sản phẩm, tổng sổ cái khớp trước / sau (docs/04) |
| 7 Dữ liệu gieo: 500 đơn mọi trạng thái trong 90 ngày, 800 đánh giá, qua đúng máy trạng thái và PricingEngine | 00 #136, `OrderSampleSeeder` | L054 (kiểm SQL trên DB mới) |
| III.9 Tài khoản phụ: mời nhân viên, vai trò & quyền theo `shop_staff`; menu Kênh Người Bán theo quyền | 00 #120, `StaffFeatures.cs`, seller → Tài khoản phụ | `An_owner_adds_a_cskh_who_works_only_within_the_granted_permissions_and_loses_access_when_removed`, `Grants_cannot_exceed_the_granters_own_and_the_owner_cannot_be_changed_or_removed`; e2e `shop-design.spec.cjs` |
| III.9 Trang trí shop (kéo thả khối banner / sản phẩm nổi bật / danh mục / video / chữ), danh mục của shop; II.5 tab Dạo, Tất cả, danh mục, Hồ sơ shop | 00 #121–122, `ShopDesignFeatures.cs`, `ShopHomeFeatures.cs`, seller → Trang trí shop / Danh mục của shop | `Shop_categories_become_tabs_with_the_shops_order_and_only_products_a_buyer_can_see`, `The_decoration_is_published_from_own_uploads_and_buyers_see_it_resolved`; e2e `shop-design.spec.cjs` |
| Giá khuyến mãi / Flash Sale trên thẻ sản phẩm khớp trang chi tiết | 00 #123, `CardPricing` | `A_running_discount_shows_on_search_shop_and_related_cards_like_on_the_product_page_and_ends_with_it`; L048 |
| II.11 chặn / báo cáo shop trong chat; quản trị xem hội thoại bị báo cáo, phạt (Phase 10 dời lại) | 00 #124, `ChatReportFeatures.cs`, web chat → Chặn / Báo cáo, admin → Chat bị báo cáo | `A_reported_chat_is_reviewed_with_an_audit_row_and_two_admins_deciding_at_once_penalize_only_once`; e2e `chat.spec.cjs` |
| VI.8 mẫu thông báo cho từng sự kiện đơn hàng (Phase 12 dời lại) | 00 #125, `TemplateCatalog` `ORDER.*`, `OrderEventHandler` | `Order_notifications_use_the_template_the_platform_edited_and_unknown_placeholders_are_refused` |
| I.2 Ảnh đại diện (cắt ảnh, ≤ 1 MB) hiện ở header & tài khoản; Quyền riêng tư: tải dữ liệu của tôi, yêu cầu xoá tài khoản có điều kiện chặn (Phase 2 dời lại) | 00 #126–127, `AvatarEditor`, `/tai-khoan/quyen-rieng-tu`, `AccountDeletionGuards.cs` | `Deleting_the_account_waits_for_open_orders_and_the_wallet_and_the_export_holds_the_orders`; e2e `account.spec.cjs`; L049, L050 |
| 3.4 / II.4 giới hạn mua mỗi người (trang sản phẩm, giỏ, thanh toán, đặt hàng song song); "Sản phẩm tương tự" cho dòng hết hàng (Phase 5 dời lại) | 00 #128–129, `PurchaseLimits`, seller → sửa sản phẩm "Giới hạn mua mỗi người" | `The_purchase_limit_counts_cart_and_past_orders_and_two_checkouts_at_once_cannot_both_pass` (đỏ khi bỏ khoá theo người mua); e2e `cart-rules.spec.cjs` |
| II.3 Lọc khoảng giá & sắp xếp theo giá đúng giá đang bán (giảm giá shop, Flash Sale) | 00 #138, `ProductSearchProjection`, `PriceIndexJob`, `SearchSyncSkusHandler`, `PostgresProductSearch` | `Price_sort_and_range_use_the_discounted_price_and_the_index_follows_a_programme_that_ends` (Meilisearch + PostgreSQL); e2e sắp xếp theo giá trên bản cài mới (L057) |
| 3.5 Đa kho: mỗi kho gửi một kiện — báo giá, vận đơn, tồn kho, phiếu giao theo từng kiện; kiện hoàn về hoàn tiền riêng | 00 #139, `Parcels`, `OrderParcels`, `ShipmentEventProcessor.ApplyParcelAsync`, `ReturnRequest.ForUndeliveredParcel`, seller → Kho hàng & vận chuyển, trang thanh toán / chi tiết đơn | `MultiWarehouseTests` (6), `ParcelsTests` (6); e2e `shop-design.spec.cjs` "Đa kho" |
| III.9 Kho hàng & địa chỉ trả hàng; III.1 / IV chọn đơn vị vận chuyển, tắt COD theo shop; III.3 đơn vị vận chuyển cho sản phẩm | 00 #140, `ShopLogisticsFeatures.cs`, `ShopChannels`, seller → Kho hàng & vận chuyển, sửa sản phẩm → Vận chuyển | `Shop_carrier_choices_and_product_carriers_decide_the_options_and_cod`, `Warehouses_keep_one_default_each_and_cannot_vanish_under_waiting_parcels_or_other_shops` |
| II.4 Phí vận chuyển ước tính tới địa chỉ mặc định, đổi địa chỉ xem lại phí; voucher của shop (Lưu); chia sẻ (sao chép, Facebook, ứng dụng của máy); shop "online … trước" | 00 #141–142, `ShippingEstimateHandler`, `ShopVouchers`, `ProductShipping`, `ShareProduct` | `The_product_page_quotes_shipping_to_my_default_address_or_a_picked_province_with_the_shops_carriers`, `The_shop_page_lists_running_programmes_and_the_product_page_the_shops_last_activity`; e2e `storefront-extras.spec.cjs` |
| II.5 Voucher shop, chương trình đang chạy, tìm trong shop | 00 #143, `ShopOffersHandler`, trang shop | e2e `storefront-extras.spec.cjs` |
| II.3 Khối "Shop liên quan đến từ khoá"; II.2 banner ngành, thương hiệu nổi bật | 00 #142, `RelatedShopsHandler`, `GetCategoryBySlugHandler` | `A_keyword_shows_the_matching_shop_and_the_category_page_its_banners_and_best_selling_brands`; e2e `storefront-extras.spec.cjs` |
| II.9 `/tai-khoan/da-xem`, `/tai-khoan/shop-theo-doi`; II.6 "Bạn có thể thích" trong giỏ | `BrowsingPages.tsx`, `CartPage` | e2e `storefront-extras.spec.cjs` (lần đầu đỏ — L059) |
| 3.11 Lọc đánh giá theo phân loại | 00 #143, `ProductReviewsHandler` | `Reviews_can_be_filtered_by_the_variant_bought_and_the_summary_counts_each_variant` |
| II.3 Facet đơn vị vận chuyển, dịch vụ (Freeship Xtra, có voucher, COD) có số đếm thật; nơi bán theo kho gửi | 00 #144, `ProductSearchProjection`, `MeiliProductSearch`, `PostgresProductSearch`, trang tìm kiếm | `Carrier_and_service_facets_count_and_filter_the_same_on_both_engines_and_follow_the_shop`, `Noi_ban_is_the_province_the_product_ships_from` (đỏ khi gỡ trigger / kho gửi); e2e `storefront-extras.spec.cjs` "Facet" |
| VII Thanh toán thất bại; sản phẩm bị khoá (trong app + kênh theo cài đặt); sản phẩm yêu thích có hàng lại; khuyến mãi ≤ 1 tin/ngày/người | 00 #145, `PaymentProcessor`, `ProductEventHandler`, `ReminderService` | `NotificationEventsTests` (3; giới hạn khuyến mãi đỏ khi gỡ) |
| III.2 Trả hàng chờ xử lý, đã xử lý, lượt truy cập, tỉ lệ chuyển đổi, thông báo của sàn; III.3 ngưỡng sắp hết hàng theo shop | 00 #146, `SellerDashboardHandler`, `LowStock`, seller → Bảng điều khiển, Thiết lập shop | `Shop_vouchers_show_saves_uses_orders_and_sales_and_the_dashboard_its_to_dos_and_traffic`; e2e `shop-design.spec.cjs` "ẩn hàng loạt" |
| III.3 Lọc theo tồn kho / giá, ẩn / hiện / xoá / gửi duyệt hàng loạt, sao chép; III.5 lượt lưu & lượt dùng & doanh số của voucher | 00 #146, `BulkProductActionHandler`, `CopyProductHandler`, `ListShopVouchersHandler` | `Products_filter_by_stock_and_price_act_in_bulk_and_copy_as_a_stockless_draft`; e2e `shop-design.spec.cjs` |
| 6.4 Dọn giỏ khách cũ; VI.8 danh mục hành chính; 6.1 giới hạn tốc độ tìm kiếm | 00 #147, `CartCleanupJob`, `DivisionAdminFeatures.cs`, admin → Nội dung → Danh mục hành chính, chính sách `search` | `Old_guest_carts_are_cleaned_and_signed_in_carts_kept`, `Admins_add_and_rename_administrative_units_without_changing_codes` (nhật ký: đỏ trước khi ghi thủ công) |
| III.3 Đăng hàng loạt bằng Excel (tệp mẫu theo ngành, kiểm từng dòng, bảng lỗi, chạy nền) và cập nhật giá / tồn hàng loạt (Phase 3 dời lại) | 00 #130–131, `BulkFeatures.cs`, `BulkTaskRunner`, `ProductSheets`, `RemoteImageFetcher`, seller → Excel hàng loạt | `An_import_sheet_creates_products_with_variants_and_images_and_lists_bad_rows_and_never_runs_twice`, `The_price_and_stock_sheet_updates_skus_with_stock_history_and_refuses_other_files_and_other_rights`, `RemoteImageFetcherTests` (SSRF); e2e `shop-design.spec.cjs` "Excel hàng loạt" (Hangfire thật) |
| 3.9 phí dịch vụ Freeship Xtra / Voucher Xtra chỉ với shop tham gia; voucher sàn chỉ phủ shop Xtra (Phase 8–9 dời lại) | 00 #132, `XtraFeatures.cs`, seller → Kênh Marketing → Chương trình dịch vụ, admin → Voucher (chỉ shop Xtra) | `An_xtra_voucher_covers_only_shops_in_the_programme_and_only_their_orders_carry_the_service_fee`; e2e `shop-design.spec.cjs` "Chương trình dịch vụ" |
| Lưu cài đặt shop không còn 500 khi việc nền cập nhật chỉ số cùng lúc | 00 #133, `SaveOwnChangesAsync`, `ConcurrencyRetry` | `Shop_settings_saves_survive_counter_updates_running_at_the_same_time`; L051 |
| II.8 "Liên hệ shop" từ đơn mua; chat gắn với yêu cầu trả hàng (Phase 7 dời lại) — thẻ đơn hàng gửi sẵn | `ContactShopButton`, `openAboutOrder` | e2e `orders.spec.cjs` "Liên hệ shop"; L052 |
| I.1 Đăng nhập Google (tuỳ chọn, cấu hình) (Phase 2 dời lại) | 00 #134, `GoogleLoginHandler`, `GoogleTokenVerifier`, `iam.user_identities`, nút Google ở Đăng nhập / Đăng ký | `A_new_google_account_needs_consent_then_signs_in_to_the_same_account_every_time`, `An_existing_account_with_the_verified_email_is_linked_not_duplicated_and_a_locked_one_stays_out`, `Forged_expired_foreign_or_unverified_tokens_are_refused` |
| 3.5 Đa kho (một kiện mỗi kho) | **không bật** — 00 #135 | — |


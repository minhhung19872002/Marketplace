# 07 — Bảng đối chiếu chức năng

Từng mục của đặc tả → nơi hiện thực → bằng chứng. Cập nhật sau mỗi phase.

## Tiến độ theo phase (mục 11)

| Phase | Nội dung | Trạng thái | Bằng chứng |
|---|---|---|---|
| 0 | Chuyển đổi repo | **Xong** | Xem bảng Phase 0 dưới đây |
| 1 | Nền móng backend | **Xong** | Xem bảng Phase 1 dưới đây |
| 2 | Tài khoản | **Xong** | Xem bảng Phase 2 dưới đây |
| 3 | Danh mục & sản phẩm | Chưa làm | |
| 4 | Tìm kiếm & trang người mua | Chưa làm | |
| 5 | Giỏ hàng & thanh toán | Chưa làm | |
| 6 | Đơn hàng & vận chuyển | Chưa làm | |
| 7 | Đánh giá, trả hàng, khiếu nại | Chưa làm | |
| 8 | Tài chính | Chưa làm | |
| 9 | Marketing | Chưa làm | |
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

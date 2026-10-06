using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Domain.Iam;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class IdentityTests(ApiFactory factory)
{
    private HttpClient Anon => factory.CreateClient();

    // ---------- Registration & OTP ----------

    [Fact]
    public async Task Phone_otp_registration_then_password_login()
    {
        var phone = ApiFactory.NewPhone();

        var registered = await factory.RegisterAsync("+84" + phone[1..], "Matkhau123", "Nguyễn Thử");
        var login = await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = phone, password = "Matkhau123" });
        var wrong = await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = phone, password = "SaiMatKhau9" });
        var unknown = await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = ApiFactory.NewPhone(), password = "SaiMatKhau9" });

        registered.User.Phone.Should().Be(phone, "+84… được chuẩn hoá thành 0…");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Same message for a wrong password and an unknown account: no account enumeration
        (await wrong.ReadEnvelopeAsync()).Message.Should().Be((await unknown.ReadEnvelopeAsync()).Message);
    }

    [Fact]
    public async Task Registration_needs_consent_strong_password_and_a_valid_ticket()
    {
        var phone = ApiFactory.NewPhone();
        var weak = await Anon.PostAsJsonAsync("/api/auth/register",
            new { target = phone, ticket = "x", password = "abcdefgh", fullName = "A", acceptTerms = false });
        var noTicket = await Anon.PostAsJsonAsync("/api/auth/register",
            new { target = phone, ticket = "khong-hop-le", password = "Matkhau123", fullName = "A", acceptTerms = true });

        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await weak.ReadEnvelopeAsync()).Errors;
        errors.Should().Contain(e => e.Field == "password" && e.Message.Contains("chữ và số"));
        errors.Should().Contain(e => e.Field == "acceptTerms");
        noTicket.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task One_ticket_registers_one_account_even_when_replayed_in_parallel()
    {
        var phone = ApiFactory.NewPhone();
        (await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" })).EnsureSuccessStatusCode();
        var code = await factory.LatestOtpAsync(phone);
        var ticket = (await (await Anon.PostAsJsonAsync("/api/auth/otp/verify", new { target = phone, purpose = "Register", code }))
            .ReadEnvelopeAsync()).Data.Str("ticket");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { target = phone, ticket, password = "Matkhau123", fullName = "Song Song", acceptTerms = true })));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Where(r => !r.IsSuccessStatusCode).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.Users.CountAsync(u => u.Phone == phone))).Should().Be(1);
    }

    [Fact]
    public async Task Registering_an_existing_phone_is_refused()
    {
        var user = await factory.CreateUserAsync();

        var response = await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = user.Phone, purpose = "Register" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadEnvelopeAsync()).Message.Should().Be("Số điện thoại đã được đăng ký.");
    }

    [Fact]
    public async Task Otp_resend_has_a_cooldown()
    {
        var phone = ApiFactory.NewPhone();
        var first = await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" });
        var again = await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ReadEnvelopeAsync()).Message.Should().StartWith("Vui lòng đợi");
    }

    [Fact]
    public async Task Parallel_otp_guessing_never_exceeds_the_attempt_cap()
    {
        var phone = ApiFactory.NewPhone();
        (await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" })).EnsureSuccessStatusCode();
        var code = await factory.LatestOtpAsync(phone);
        var wrong = code == "000000" ? "111111" : "000000";

        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => factory.CreateClient().PostAsJsonAsync("/api/auth/otp/verify",
            new { target = phone, purpose = "Register", code = wrong })));
        var correctAfterwards = await Anon.PostAsJsonAsync("/api/auth/otp/verify", new { target = phone, purpose = "Register", code });

        (await factory.WithDbAsync(db => db.OtpCodes.Where(o => o.Target == phone).Select(o => o.Attempts).SingleAsync()))
            .Should().Be(5, "số lần sai bị chặn đúng ở AUTH.OTP_MAX_ATTEMPTS dù gửi song song");
        correctAfterwards.StatusCode.Should().Be(HttpStatusCode.Conflict, "mã đã bị khoá sau 5 lần sai");
    }

    [Fact]
    public async Task Otp_is_stored_hashed_never_in_clear()
    {
        var phone = ApiFactory.NewPhone();
        (await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" })).EnsureSuccessStatusCode();
        var code = await factory.LatestOtpAsync(phone);

        var stored = await factory.WithDbAsync(db => db.OtpCodes.Where(o => o.Target == phone).Select(o => o.CodeHash).SingleAsync());

        stored.Should().NotContain(code).And.HaveLength(64);
    }

    // ---------- Login hardening ----------

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_temporarily()
    {
        var user = await factory.CreateUserAsync();

        for (var i = 0; i < 5; i++)
            await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = user.Phone, password = "SaiMatKhau9" });
        var correct = await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = user.Phone, password = ApiFactory.DefaultPassword });

        correct.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await correct.ReadEnvelopeAsync()).Message.Should().Contain("tạm khoá");
    }

    [Fact]
    public async Task Parallel_wrong_passwords_still_trigger_the_lockout()
    {
        var user = await factory.CreateUserAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { identifier = user.Phone, password = "SaiMatKhau9" })));

        var lockedUntil = await factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.LockedUntil).SingleAsync());
        lockedUntil.Should().NotBeNull().And.BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_with_otp()
    {
        var user = await factory.CreateUserAsync();
        (await Anon.PostAsJsonAsync("/api/auth/otp/send", new { target = user.Phone, purpose = "Login" })).EnsureSuccessStatusCode();
        var code = await factory.LatestOtpAsync(user.Phone);

        var login = await Anon.PostAsJsonAsync("/api/auth/login-otp", new { phone = user.Phone, code });
        var replay = await Anon.PostAsJsonAsync("/api/auth/login-otp", new { phone = user.Phone, code });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict, "mã OTP chỉ dùng một lần");
    }

    // ---------- Sessions ----------

    [Fact]
    public async Task Refresh_rotates_and_a_replayed_token_kills_the_whole_session()
    {
        var user = await factory.CreateUserAsync();

        var first = await Anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });
        var rotated = (await first.ReadEnvelopeAsync<LoginData>()).Data!;
        var replay = await Anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });
        var newTokenAfterReplay = await Anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken });
        var accessAfterReplay = await factory.Authorized(rotated.AccessToken).GetAsync("/api/account/me");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        rotated.RefreshToken.Should().NotBe(user.RefreshToken);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        newTokenAfterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "phát hiện dùng lại → thu hồi cả chuỗi");
        accessAfterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.WithDbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokeReason == RevokeReasons.ReuseDetected)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Parallel_refresh_of_one_token_succeeds_once()
    {
        var user = await factory.CreateUserAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken })));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
    }

    [Fact]
    public async Task Refresh_also_works_from_the_httponly_cookie()
    {
        var phone = (await factory.CreateUserAsync()).Phone;
        var client = factory.CreateClient(new() { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier = phone, password = ApiFactory.DefaultPassword });
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("sh_rt="));

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });

        setCookie.Should().Contain("httponly").And.Contain("path=/api/auth").And.Contain("samesite=strict");
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_ends_the_session_immediately()
    {
        var user = await factory.CreateUserAsync();
        (await user.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Anon.PostAsJsonAsync("/api/auth/logout", new { refreshToken = user.RefreshToken })).EnsureSuccessStatusCode();

        (await user.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Locking_a_signed_in_user_rejects_their_very_next_request()
    {
        var buyer = await factory.CreateUserAsync();
        var admin = await factory.CreateUserAsync(Permissions.UserLock);
        (await buyer.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.Client.PostAsJsonAsync($"/api/admin/users/{buyer.Id}/lock", new { reason = "Gian lận" })).EnsureSuccessStatusCode();

        (await buyer.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var relogin = await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = buyer.Phone, password = ApiFactory.DefaultPassword });
        (await relogin.ReadEnvelopeAsync()).Message.Should().Contain("đã bị khoá");

        (await admin.Client.PostAsync($"/api/admin/users/{buyer.Id}/unlock", null)).EnsureSuccessStatusCode();
        (await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = buyer.Phone, password = ApiFactory.DefaultPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_password_signs_out_other_devices_only()
    {
        var user = await factory.CreateUserAsync();
        var otherDevice = await factory.LoginAsync(user.Phone, ApiFactory.DefaultPassword);

        var change = await user.Client.PutAsJsonAsync("/api/account/password",
            new { currentPassword = ApiFactory.DefaultPassword, newPassword = "MatKhauMoi456" });

        change.StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.Authorized(otherDevice.AccessToken).GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = otherDevice.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forgot_password_resets_and_ends_every_session()
    {
        var user = await factory.CreateUserAsync();

        (await Anon.PostAsJsonAsync("/api/auth/forgot-password", new { target = user.Phone })).EnsureSuccessStatusCode();
        var code = await factory.LatestOtpAsync(user.Phone);
        var ticket = (await (await Anon.PostAsJsonAsync("/api/auth/otp/verify", new { target = user.Phone, purpose = "ResetPassword", code }))
            .ReadEnvelopeAsync()).Data.Str("ticket");
        var reset = await Anon.PostAsJsonAsync("/api/auth/reset-password", new { target = user.Phone, ticket, newPassword = "DatLai2026x" });

        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Anon.PostAsJsonAsync("/api/auth/login", new { identifier = user.Phone, password = "DatLai2026x" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_for_unknown_numbers_and_sends_nothing()
    {
        var phone = ApiFactory.NewPhone();

        var response = await Anon.PostAsJsonAsync("/api/auth/forgot-password", new { target = phone });
        await factory.DispatchOutboxAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.SimulatedSms.AnyAsync(s => s.To == phone))).Should().BeFalse();
    }

    [Fact]
    public async Task Devices_list_and_remote_logout_with_ownership()
    {
        var user = await factory.CreateUserAsync();
        var phoneDevice = await factory.LoginAsync(user.Phone, ApiFactory.DefaultPassword);
        var stranger = await factory.CreateUserAsync();

        var sessions = (await (await user.Client.GetAsync("/api/account/sessions")).ReadEnvelopeAsync()).Data.EnumerateArray().ToList();
        var other = sessions.Single(s => !s.GetProperty("isCurrent").GetBoolean());
        var strangerTry = await stranger.Client.DeleteAsync($"/api/account/sessions/{other.Str("id")}");
        var revoke = await user.Client.DeleteAsync($"/api/account/sessions/{other.Str("id")}");

        sessions.Should().HaveCount(2);
        strangerTry.StatusCode.Should().Be(HttpStatusCode.NotFound, "phiên của người khác → 404, không phải 403");
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.Authorized(phoneDevice.AccessToken).GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- Profile & privacy ----------

    [Fact]
    public async Task Profile_update_and_phone_change_via_otp()
    {
        var user = await factory.CreateUserAsync();
        var newPhone = ApiFactory.NewPhone();

        (await user.Client.PutAsJsonAsync("/api/account/profile", new { fullName = "Tên Mới", gender = "Female", dateOfBirth = "1995-05-20" }))
            .EnsureSuccessStatusCode();
        (await user.Client.PostAsJsonAsync("/api/account/contact/otp", new { newValue = newPhone })).EnsureSuccessStatusCode();
        var code = await factory.LatestOtpAsync(newPhone);
        (await user.Client.PutAsJsonAsync("/api/account/contact", new { newValue = newPhone, code })).EnsureSuccessStatusCode();

        var me = (await (await user.Client.GetAsync("/api/account/me")).ReadEnvelopeAsync()).Data;
        me.Str("fullName").Should().Be("Tên Mới");
        me.Str("gender").Should().Be("Female");
        me.Str("phone").Should().Be(newPhone);
    }

    [Fact]
    public async Task Deleting_the_account_anonymises_it_and_frees_the_number()
    {
        var user = await factory.CreateUserAsync();

        var wrongPassword = await user.Client.PostAsJsonAsync("/api/account/delete", new { password = "khongdung1" });
        var delete = await user.Client.PostAsJsonAsync("/api/account/delete", new { password = ApiFactory.DefaultPassword });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var row = await factory.WithDbAsync(db => db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id));
        row.Phone.Should().BeNull();
        row.FullName.Should().Be("Người dùng đã xoá");
        // The number can register again
        await factory.RegisterAsync(user.Phone);
    }

    // ---------- Address book ----------

    private static object Address(string name = "Người Nhận", bool isDefault = false, string ward = "00001") => new
    {
        receiverName = name,
        phone = "0912345678",
        provinceCode = "01",
        districtCode = "001",
        wardCode = ward,
        street = "12 Phố Thử",
        type = "Home",
        isDefault,
    };

    [Fact]
    public async Task First_address_becomes_default_and_hierarchy_is_validated()
    {
        var user = await factory.CreateUserAsync();

        var first = await user.Client.PostAsJsonAsync("/api/account/addresses", Address("A"));
        var wrongWard = await user.Client.PostAsJsonAsync("/api/account/addresses", Address("B", ward: "26734"));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await first.ReadEnvelopeAsync()).Data;
        created.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        created.Str("provinceName").Should().Be("Thành phố Hà Nội");
        wrongWard.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongWard.ReadEnvelopeAsync()).Errors.Should().Contain(e => e.Field == "wardCode");
    }

    [Fact]
    public async Task Address_limit_is_enforced()
    {
        var user = await factory.CreateUserAsync();
        for (var i = 0; i < 10; i++)
            (await user.Client.PostAsJsonAsync("/api/account/addresses", Address($"N{i}"))).EnsureSuccessStatusCode();

        var eleventh = await user.Client.PostAsJsonAsync("/api/account/addresses", Address("N10"));

        eleventh.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await eleventh.ReadEnvelopeAsync()).Message.Should().Contain("tối đa 10");
    }

    [Fact]
    public async Task Someone_elses_address_is_404_for_every_action()
    {
        var owner = await factory.CreateUserAsync();
        var stranger = await factory.CreateUserAsync();
        var id = (await (await owner.Client.PostAsJsonAsync("/api/account/addresses", Address())).ReadEnvelopeAsync()).Data.Str("id");

        (await stranger.Client.PutAsJsonAsync($"/api/account/addresses/{id}", Address("Chiếm"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.PostAsync($"/api/account/addresses/{id}/default", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.DeleteAsync($"/api/account/addresses/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = (await (await stranger.Client.GetAsync("/api/account/addresses")).ReadEnvelopeAsync()).Data;
        list.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Parallel_set_default_leaves_exactly_one_default()
    {
        var user = await factory.CreateUserAsync();
        var ids = new List<string>();
        for (var i = 0; i < 6; i++)
            ids.Add((await (await user.Client.PostAsJsonAsync("/api/account/addresses", Address($"N{i}"))).ReadEnvelopeAsync()).Data.Str("id"));

        await Task.WhenAll(ids.Select(id => factory.Authorized(user.AccessToken).PostAsync($"/api/account/addresses/{id}/default", null)));

        (await factory.WithDbAsync(db => db.Addresses.CountAsync(a => a.UserId == user.Id && a.IsDefault))).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_the_default_promotes_another_address()
    {
        var user = await factory.CreateUserAsync();
        var first = (await (await user.Client.PostAsJsonAsync("/api/account/addresses", Address("A"))).ReadEnvelopeAsync()).Data.Str("id");
        (await user.Client.PostAsJsonAsync("/api/account/addresses", Address("B"))).EnsureSuccessStatusCode();

        (await user.Client.DeleteAsync($"/api/account/addresses/{first}")).EnsureSuccessStatusCode();

        var list = (await (await user.Client.GetAsync("/api/account/addresses")).ReadEnvelopeAsync()).Data.EnumerateArray().ToList();
        list.Should().ContainSingle().Which.GetProperty("isDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Administrative_divisions_are_complete_and_hierarchical()
    {
        var provinces = (await (await Anon.GetAsync("/api/admin-divisions")).ReadEnvelopeAsync()).Data;
        var hanoiDistricts = (await (await Anon.GetAsync("/api/admin-divisions?parent=01")).ReadEnvelopeAsync()).Data;

        provinces.GetArrayLength().Should().Be(63);
        hanoiDistricts.EnumerateArray().Should().Contain(d => d.Str("name") == "Quận Ba Đình");
        (await factory.WithDbAsync(db => db.AdminDivisions.CountAsync(d => d.Level == AdminDivisionLevel.Ward))).Should().BeGreaterThan(10_000);
    }

    // ---------- Admin RBAC ----------

    [Fact]
    public async Task Seeded_admin_must_change_password_before_doing_anything_else()
    {
        // Give the seeded admin a known password (the real one is random and only printed to the log)
        await factory.WithDbAsync(async db =>
        {
            var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
            var admin = await db.Users.SingleAsync(u => u.Username == "admin");
            admin.ChangePassword(hasher.Hash("Khoitao123"));
            admin.RequirePasswordChange();
            await db.SaveChangesAsync();
        });

        var first = await factory.LoginAsync("admin", "Khoitao123");
        var client = factory.Authorized(first.AccessToken);
        var blocked = await client.GetAsync("/api/admin/users");
        var me = await client.GetAsync("/api/account/me");
        var change = await client.PutAsJsonAsync("/api/account/password", new { currentPassword = "Khoitao123", newPassword = "QuanTri2026x" });
        var second = await factory.LoginAsync("admin", "QuanTri2026x");
        var allowed = await factory.Authorized(second.AccessToken).GetAsync("/api/admin/users");

        first.User.MustChangePassword.Should().BeTrue();
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.ReadEnvelopeAsync()).Message.Should().Be("Bạn cần đổi mật khẩu trước khi tiếp tục.");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        change.StatusCode.Should().Be(HttpStatusCode.OK);
        second.User.MustChangePassword.Should().BeFalse();
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Role_permissions_reach_the_token_and_the_last_super_admin_is_protected()
    {
        var manager = await factory.CreateUserAsync(Permissions.RoleManage, Permissions.RoleView, Permissions.UserAssignRole);
        var target = await factory.CreateUserAsync();

        var create = await manager.Client.PostAsJsonAsync("/api/admin/roles",
            new { code = $"KIEM_{Random.Shared.Next(100000)}", name = "Kiểm duyệt", description = "", permissions = new[] { Permissions.UserView } });
        var roleId = (await create.ReadEnvelopeAsync()).Data.GetString();
        (await manager.Client.PutAsJsonAsync($"/api/admin/users/{target.Id}/roles", new { roleIds = new[] { roleId } })).EnsureSuccessStatusCode();
        var relogin = await factory.LoginAsync(target.Phone, ApiFactory.DefaultPassword);
        var canView = await factory.Authorized(relogin.AccessToken).GetAsync("/api/admin/users");
        var cannotLock = await factory.Authorized(relogin.AccessToken).PostAsJsonAsync($"/api/admin/users/{manager.Id}/lock", new { reason = "x" });

        var superAdminRole = await factory.WithDbAsync(db => db.Roles.Where(r => r.Code == RoleCatalog.SuperAdmin).Select(r => r.Id).SingleAsync());
        var adminId = await factory.WithDbAsync(db => db.Users.Where(u => u.Username == "admin").Select(u => u.Id).SingleAsync());
        var stripLast = await manager.Client.PutAsJsonAsync($"/api/admin/users/{adminId}/roles", new { roleIds = Array.Empty<Guid>() });

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        canView.StatusCode.Should().Be(HttpStatusCode.OK);
        cannotLock.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        stripLast.StatusCode.Should().Be(HttpStatusCode.Conflict, "phải còn ít nhất một Quản trị cao nhất");
        superAdminRole.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Admin_user_search_pages_and_filters_in_sql()
    {
        var admin = await factory.CreateUserAsync(Permissions.UserView);
        var target = await factory.CreateUserAsync();

        var byPhone = (await (await admin.Client.GetAsync($"/api/admin/users?q={target.Phone}")).ReadEnvelopeAsync()).Data;
        var page = (await (await admin.Client.GetAsync("/api/admin/users?page=1&pageSize=3")).ReadEnvelopeAsync()).Data;

        byPhone.GetProperty("items").EnumerateArray().Should().ContainSingle(u => u.Str("id") == target.Id.ToString());
        page.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(3);
        page.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task Seeded_credentials_never_reach_the_log_table()
    {
        // Positive control: provoke a warning we know is logged, then wait until the batched sink has flushed it
        var marker = $"khong.co.bo.xu.ly.{Guid.NewGuid():N}";
        await factory.WithDbAsync(async db =>
        {
            db.OutboxMessages.Add(new ShopHub.Domain.SystemConfig.OutboxMessage(marker, "{}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
        await factory.DispatchOutboxAsync();

        async Task<long> CountAsync(string where) => await factory.WithDbAsync(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT count(*) FROM sys.logs WHERE {where}";
            return (long)(await cmd.ExecuteScalarAsync())!;
        });

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await CountAsync($"properties::text LIKE '%{marker}%'") == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(500);

        (await CountAsync($"properties::text LIKE '%{marker}%'")).Should().BeGreaterThan(0, "sink PostgreSQL phải đang ghi");
        (await CountAsync("message ILIKE '%mật khẩu%' OR message ILIKE '%password%'"))
            .Should().Be(0, "mật khẩu sinh ra khi gieo dữ liệu chỉ in ra stdout, không qua Serilog");
    }

    [Fact]
    public async Task Audit_journal_masks_password_hashes()
    {
        var user = await factory.CreateUserAsync();
        (await user.Client.PutAsJsonAsync("/api/account/password",
            new { currentPassword = ApiFactory.DefaultPassword, newPassword = "MatKhauMoi789" })).EnsureSuccessStatusCode();

        var entries = await factory.WithDbAsync(db => db.AuditLogs
            .Where(a => a.Entity == nameof(User) && a.EntityId == user.Id.ToString()).Select(a => a.NewValue).ToListAsync());

        entries.Should().NotBeEmpty();
        entries.Should().OnlyContain(v => v == null || (!v.Contains("$2a$") && !v.Contains("$2b$")));
        entries.Should().Contain(v => v != null && v.Contains("PasswordHash") && v.Contains("***"));
    }
}

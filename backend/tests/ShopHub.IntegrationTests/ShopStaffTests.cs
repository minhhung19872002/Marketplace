using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Security;
using ShopHub.Domain.Shops;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Tài khoản phụ của shop (III.9): invite, accept, scope by grants, change, remove.</summary>
[Collection(ApiCollection.Name)]
public class ShopStaffTests(ApiFactory factory)
{
    [Fact]
    public async Task An_owner_adds_a_cskh_who_works_only_within_the_granted_permissions_and_loses_access_when_removed()
    {
        var store = await factory.CreateStoreAsync();
        var owner = await OwnerAsync(store);
        var staff = await factory.CreateUserAsync();
        var basePath = $"/api/seller/shops/{store.ShopId}";

        var staffId = await InviteAndAcceptAsync(owner, basePath, staff, new { login = staff.Phone, role = "CustomerService" });

        // The new staff member sees the shop with the role's default grants
        var mine = (await (await staff.Client.GetAsync("/api/seller/shops")).ReadEnvelopeAsync()).Data;
        var shop = mine.EnumerateArray().Single(s => s.Str("id") == store.ShopId.ToString());
        shop.Str("role").Should().Be("CustomerService");
        shop.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo(ShopPermissions.DefaultsFor(ShopStaffRole.CustomerService));
        // Within the grants: orders; outside them: settings and staff management
        (await staff.Client.GetAsync($"{basePath}/orders")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.Client.PutAsJsonAsync($"{basePath}/vacation", new { until = DateTimeOffset.UtcNow.AddDays(2) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync($"{basePath}/staff-accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Promote to manager: settings now allowed
        (await owner.PutAsJsonAsync($"{basePath}/staff-accounts/{staffId}", new { role = "Manager" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.Client.PutAsJsonAsync($"{basePath}/vacation", new { until = DateTimeOffset.UtcNow.AddDays(2) })).StatusCode.Should().Be(HttpStatusCode.OK);

        var board = (await (await owner.GetAsync($"{basePath}/staff-accounts")).ReadEnvelopeAsync()).Data;
        board.GetProperty("staff").GetArrayLength().Should().Be(2);
        board.GetProperty("staff").EnumerateArray().Single(s => s.Str("id") == staffId.ToString()).Str("phoneMasked")
            .Should().NotContain(staff.Phone[3..^3]);

        // Removed: the very next call is "not your shop"
        (await owner.DeleteAsync($"{basePath}/staff-accounts/{staffId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.Client.GetAsync($"{basePath}/orders")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await staff.Client.GetAsync("/api/seller/shops")).ReadEnvelopeAsync()).Data.GetArrayLength().Should().Be(0);

        // …and can be invited again later (the unique index skips removed rows)
        await InviteAndAcceptAsync(owner, basePath, staff, new { login = staff.Phone, role = "Warehouse" });
    }

    [Fact]
    public async Task Grants_cannot_exceed_the_granters_own_and_the_owner_cannot_be_changed_or_removed()
    {
        var store = await factory.CreateStoreAsync();
        var owner = await OwnerAsync(store);
        var basePath = $"/api/seller/shops/{store.ShopId}";
        var manager = await factory.CreateUserAsync();
        var managerId = await InviteAndAcceptAsync(owner, basePath, manager, new
        {
            login = manager.Phone, role = "Manager", permissions = new[] { ShopPermissions.StaffManage, ShopPermissions.OrderView },
        });

        var other = await factory.CreateUserAsync();
        // A manager holding STAFF.MANAGE but not FINANCE.WITHDRAW cannot hand it out
        var tooMuch = await manager.Client.PostAsJsonAsync($"{basePath}/staff-accounts",
            new { login = other.Phone, role = "Warehouse", permissions = new[] { ShopPermissions.FinanceWithdraw } });
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.Client.PostAsJsonAsync($"{basePath}/staff-accounts",
            new { login = other.Phone, role = "Warehouse", permissions = new[] { ShopPermissions.OrderView } })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Nobody becomes a second owner; the owner row and one's own row are not editable
        (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = (await factory.CreateUserAsync()).Phone, role = "Owner" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var ownerRow = (await (await owner.GetAsync($"{basePath}/staff-accounts")).ReadEnvelopeAsync()).Data.GetProperty("staff")
            .EnumerateArray().Single(s => s.Str("role") == "Owner").Str("id");
        (await manager.Client.DeleteAsync($"{basePath}/staff-accounts/{ownerRow}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await manager.Client.PutAsJsonAsync($"{basePath}/staff-accounts/{managerId}", new { role = "Manager", permissions = ShopPermissions.All }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Unknown account → 404, already staff → 409, staff of shop X cannot manage shop Y → 404
        (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = "0999999999", role = "Warehouse" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = manager.Phone, role = "Warehouse" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var otherStore = await factory.CreateStoreAsync();
        (await manager.Client.GetAsync($"/api/seller/shops/{otherStore.ShopId}/staff-accounts")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_join_only_by_accepting_an_invitation_that_has_not_expired_or_been_revoked()
    {
        var store = await factory.CreateStoreAsync();
        var owner = await OwnerAsync(store);
        var basePath = $"/api/seller/shops/{store.ShopId}";
        var invitee = await factory.CreateUserAsync();

        var invite = await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = invitee.Phone, role = "Warehouse" });
        invite.StatusCode.Should().Be(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());
        var invitationId = (await invite.ReadEnvelopeAsync<Guid>()).Data;

        // Not a member yet: no shop, no access, but an invitation (and a notification) waiting
        (await (await invitee.Client.GetAsync("/api/seller/shops")).ReadEnvelopeAsync()).Data.GetArrayLength().Should().Be(0);
        (await invitee.Client.GetAsync($"{basePath}/orders")).StatusCode.Should().Be(HttpStatusCode.NotFound, "chưa đồng ý thì chưa vào shop");
        var waiting = (await (await invitee.Client.GetAsync("/api/seller/staff-invitations")).ReadEnvelopeAsync()).Data.EnumerateArray().ToList();
        waiting.Should().ContainSingle().Which.Str("shopName").Should().NotBeNullOrEmpty();
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == invitee.Id && n.RefId == invitationId))).Should().Be(1);
        var board = (await (await owner.GetAsync($"{basePath}/staff-accounts")).ReadEnvelopeAsync()).Data;
        board.GetProperty("invitations").EnumerateArray().Select(i => i.Str("id")).Should().Equal(invitationId.ToString());
        (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = invitee.Phone, role = "Manager" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "đã có lời mời đang chờ");

        // Someone else cannot accept it; the shop can revoke it, after which it cannot be accepted
        var stranger = await factory.CreateUserAsync();
        (await stranger.Client.PostAsync($"/api/seller/staff-invitations/{invitationId}/accept", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"{basePath}/staff-invitations/{invitationId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await invitee.Client.PostAsync($"/api/seller/staff-invitations/{invitationId}/accept", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // An expired invitation cannot be accepted either
        var again = (await (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = invitee.Phone, role = "Warehouse" }))
            .ReadEnvelopeAsync<Guid>()).Data;
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE shop.shop_staff_invitations SET expires_at = now() - interval '1 minute' WHERE id = {again}"));
        var late = await invitee.Client.PostAsync($"/api/seller/staff-invitations/{again}/accept", null);
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await late.Content.ReadAsStringAsync()).Should().Contain("hết hạn");

        // Declined → gone; a fresh one accepted → member with the invited role
        var third = (await (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = invitee.Phone, role = "Warehouse" }))
            .ReadEnvelopeAsync<Guid>()).Data;
        (await invitee.Client.PostAsync($"/api/seller/staff-invitations/{third}/decline", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await invitee.Client.GetAsync("/api/seller/staff-invitations")).ReadEnvelopeAsync()).Data.GetArrayLength().Should().Be(0);
        await InviteAndAcceptAsync(owner, basePath, invitee, new { login = invitee.Phone, role = "Warehouse" });
        var shop = (await (await invitee.Client.GetAsync("/api/seller/shops")).ReadEnvelopeAsync()).Data.EnumerateArray().Single();
        shop.Str("role").Should().Be("Warehouse");
    }

    /// <summary>Invite (the shop) then accept (the invitee); returns the new staff row id.</summary>
    private static async Task<Guid> InviteAndAcceptAsync(HttpClient inviter, string basePath, TestUser invitee, object body)
    {
        var invite = await inviter.PostAsJsonAsync($"{basePath}/staff-accounts", body);
        invite.StatusCode.Should().Be(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());
        var invitationId = (await invite.ReadEnvelopeAsync<Guid>()).Data;
        var accept = await invitee.Client.PostAsync($"/api/seller/staff-invitations/{invitationId}/accept", null);
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return (await accept.ReadEnvelopeAsync<Guid>()).Data;
    }

    // A signed-in user holding the owner row of the store (the fixture's own owner has no session)
    private async Task<HttpClient> OwnerAsync(TestStore store)
    {
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            var row = await db.ShopStaff.SingleAsync(s => s.ShopId == store.ShopId && s.Role == ShopStaffRole.Owner);
            db.ShopStaff.Remove(row);
            db.ShopStaff.Add(new ShopStaff(store.ShopId, user.Id, ShopStaffRole.Owner, ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return user.Client;
    }
}

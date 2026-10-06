using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Security;
using ShopHub.Domain.Shops;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Tài khoản phụ của shop (III.9): add, scope by grants, change, remove.</summary>
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

        var add = await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = staff.Phone, role = "CustomerService" });
        add.StatusCode.Should().Be(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        var staffId = (await add.ReadEnvelopeAsync<Guid>()).Data;

        // The new staff member sees the shop with the role's default grants, and got an in-app notification
        var mine = (await (await staff.Client.GetAsync("/api/seller/shops")).ReadEnvelopeAsync()).Data;
        var shop = mine.EnumerateArray().Single(s => s.Str("id") == store.ShopId.ToString());
        shop.Str("role").Should().Be("CustomerService");
        shop.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo(ShopPermissions.DefaultsFor(ShopStaffRole.CustomerService));
        await factory.WithDbAsync(async db =>
            (await db.Notifications.CountAsync(n => n.UserId == staff.Id && n.RefId == staffId)).Should().Be(1));

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

        // …and can be added again later (the unique index skips removed rows)
        (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new { login = staff.Phone, role = "Warehouse" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Grants_cannot_exceed_the_granters_own_and_the_owner_cannot_be_changed_or_removed()
    {
        var store = await factory.CreateStoreAsync();
        var owner = await OwnerAsync(store);
        var basePath = $"/api/seller/shops/{store.ShopId}";
        var manager = await factory.CreateUserAsync();
        var managerId = (await (await owner.PostAsJsonAsync($"{basePath}/staff-accounts", new
        {
            login = manager.Phone, role = "Manager", permissions = new[] { ShopPermissions.StaffManage, ShopPermissions.OrderView },
        })).ReadEnvelopeAsync<Guid>()).Data;

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

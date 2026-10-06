using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Shop settings saved while the system updates the shop's counters at the same moment (L051).</summary>
[Collection(ApiCollection.Name)]
public class ShopSettingsConcurrencyTests(ApiFactory factory)
{
    [Fact]
    public async Task Shop_settings_saves_survive_counter_updates_running_at_the_same_time()
    {
        var store = await factory.CreateStoreAsync();
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, user.Id, Domain.Shops.ShopStaffRole.Manager, Application.Security.ShopPermissions.All));
            await db.SaveChangesAsync();
        });

        // What recomputing follower / product counts does to the row (every 25 ms — far busier than the real jobs) while the seller saves settings
        using var stop = new CancellationTokenSource();
        var churn = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE shop.shops SET follower_count = follower_count WHERE id = {store.ShopId}"));
                await Task.Delay(25);
            }
        });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 30; i++)
        {
            statuses.Add((await user.Client.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/xtra/FreeshipXtra", new { join = i % 2 == 0 })).StatusCode);
            statuses.Add((await user.Client.PutAsJsonAsync($"/api/seller/shops/{store.ShopId}/profile",
                new { description = $"Mô tả lần {i}", logoAssetId = (Guid?)null, coverAssetId = (Guid?)null })).StatusCode);
        }
        stop.Cancel();
        await churn;

        statuses.Should().OnlyContain(s => s == HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == store.ShopId).Select(s => s.Description).SingleAsync())).Should().Be("Mô tả lần 29");
    }
}

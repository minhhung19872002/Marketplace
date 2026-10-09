using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>G4-D: the launch checklist measures the running system; a demo install is never "ready".</summary>
[Collection(ApiCollection.Name)]
public class LaunchChecklistTests(ApiFactory factory)
{
    private async Task SetParameterAsync(string key, string value)
    {
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, value)));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
    }

    private static JsonElement Item(JsonElement data, string id) =>
        data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

    [Fact]
    public async Task The_checklist_needs_its_permission_and_blocks_a_demo_install_until_the_site_runs_live()
    {
        (await (await factory.CreateUserAsync()).Client.GetAsync("/api/admin/launch-checklist")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = await factory.CreateUserAsync(Permissions.SystemParameterView);
        try
        {
            await SetParameterAsync(ParameterKeys.SiteMode, SiteMode.Demo);
            var demo = (await (await admin.Client.GetAsync("/api/admin/launch-checklist")).ReadEnvelopeAsync()).Data;
            demo.GetProperty("ready").GetBoolean().Should().BeFalse();
            Item(demo, "site-mode").GetProperty("status").GetString().Should().Be("Fail");
            Item(demo, "site-mode").GetProperty("blocking").GetBoolean().Should().BeTrue();
            // The test install has the seed's sample legal details and no real carrier / SMS / MOIT registration
            foreach (var id in new[] { "legal", "moit", "carrier", "sms" })
                Item(demo, id).GetProperty("status").GetString().Should().Be("Fail", id);
            // A missing online gateway is only a warning: a COD-only launch is allowed
            Item(demo, "payment").GetProperty("blocking").GetBoolean().Should().BeFalse();

            await SetParameterAsync(ParameterKeys.SiteMode, SiteMode.Live);
            var live = (await (await admin.Client.GetAsync("/api/admin/launch-checklist")).ReadEnvelopeAsync()).Data;
            Item(live, "site-mode").GetProperty("status").GetString().Should().Be("Pass");
            live.GetProperty("total").GetInt32().Should().Be(live.GetProperty("items").GetArrayLength());
        }
        finally
        {
            await SetParameterAsync(ParameterKeys.SiteMode, SiteMode.Demo);
        }
    }
}

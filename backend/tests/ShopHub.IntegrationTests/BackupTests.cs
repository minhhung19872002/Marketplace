using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Ops;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 14 C3: database backups as a background job — kept count, every run recorded, failures alerted, listed for admins.</summary>
[Collection(ApiCollection.Name)]
public class BackupTests(ApiFactory factory)
{
    private async Task<BackupRunDto> RunAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BackupService>().RunAsync(CancellationToken.None);
    }

    private async Task SetParameterAsync(string key, string value)
    {
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, value)));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
    }

    [Fact]
    public async Task Backups_keep_the_newest_ones_record_every_run_alert_on_failure_and_are_listed_for_admins()
    {
        var watcher = await factory.CreateUserAsync(Permissions.JobDashboardView);
        await SetParameterAsync(ParameterKeys.BackupKeepCount, "2");
        try
        {
            var runs = new List<BackupRunDto>();
            for (var i = 0; i < 3; i++) runs.Add(await RunAsync());
            runs.Should().OnlyContain(r => r.Status == BackupStatus.Succeeded && r.SizeBytes > 0);
            var files = Directory.GetFiles(factory.BackupDirectory, "shophub-*.dump").Select(Path.GetFileName).ToList();
            files.Should().BeEquivalentTo(runs.Skip(1).Select(r => r.FileName), "chỉ giữ 2 bản mới nhất");

            factory.Dumper.FailWith = "could not connect to server";
            var failed = await RunAsync();
            failed.Status.Should().Be(BackupStatus.Failed);
            failed.Error.Should().Contain("could not connect");
            Directory.GetFiles(factory.BackupDirectory, "*.partial").Should().BeEmpty("không để lại tệp dở");
            (await factory.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == watcher.Id).Select(n => n.Title).ToListAsync()))
                .Should().Contain("Sao lưu CSDL thất bại");
        }
        finally
        {
            factory.Dumper.FailWith = null;
            await SetParameterAsync(ParameterKeys.BackupKeepCount, "14");
        }

        var overview = (await (await watcher.Client.GetAsync("/api/admin/backups")).ReadEnvelopeAsync()).Data;
        overview.GetProperty("files").GetArrayLength().Should().Be(2);
        overview.GetProperty("runs").EnumerateArray().First().Str("status").Should().Be("Failed", "lần gần nhất đứng đầu");
        (await (await factory.CreateUserAsync()).Client.GetAsync("/api/admin/backups")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

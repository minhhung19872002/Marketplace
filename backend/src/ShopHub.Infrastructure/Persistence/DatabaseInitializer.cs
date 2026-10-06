using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>Apply pending migrations (when enabled), seed missing data, register recurring jobs.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var settings = sp.GetRequiredService<ShopHubSettings>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var db = sp.GetRequiredService<ShopHubDbContext>();

        if (settings.MigrateOnStartup)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(ct);
            }
        }

        await SeedSystemParametersAsync(db, logger, ct);

        if (settings.JobsEnabled)
            await sp.GetRequiredService<IJobScheduler>().RegisterRecurringJobsAsync(ct);
    }

    // Each key is checked on its own: a partially seeded DB gets only what it lacks, existing values are kept
    private static async Task SeedSystemParametersAsync(ShopHubDbContext db, ILogger logger, CancellationToken ct)
    {
        var existing = await db.SystemParameters.IgnoreQueryFilters().Select(p => p.Key).ToListAsync(ct);
        var missing = ParameterCatalog.All.Where(d => !existing.Contains(d.Key)).ToList();
        if (missing.Count == 0) return;

        db.SystemParameters.AddRange(missing.Select(d =>
            new SystemParameter(d.Key, d.DefaultValue, d.DataType, d.Group, d.Name, d.Description)));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} system parameter(s)", missing.Count);
    }
}

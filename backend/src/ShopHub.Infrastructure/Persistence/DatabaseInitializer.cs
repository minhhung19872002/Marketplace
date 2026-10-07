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
        await sp.GetRequiredService<ShopHub.Infrastructure.Seed.IdentitySeeder>().SeedAsync(settings.SeedSampleData, ct);

        // Buckets first: the catalog seed uploads product images
        await sp.GetRequiredService<ShopHub.Infrastructure.Media.MinioObjectStorage>().EnsureBucketsAsync(ct);
        await sp.GetRequiredService<ShopHub.Infrastructure.Media.EvidenceRelocation>().RunAsync(ct);
        await sp.GetRequiredService<ShopHub.Infrastructure.Seed.CatalogSeeder>().SeedAsync(settings.SeedSampleData, ct);
        await sp.GetRequiredService<ShopHub.Infrastructure.Seed.CommerceSeeder>().SeedAsync(settings.SeedSampleData, ct);
        await sp.GetRequiredService<ShopHub.Infrastructure.Seed.ContentSeeder>().SeedAsync(ct);
        await sp.GetRequiredService<ShopHub.Infrastructure.Seed.FinanceSeeder>().SeedAsync(ct);
        if (settings.SeedSampleData) await sp.GetRequiredService<ShopHub.Infrastructure.Seed.MarketingSeeder>().SeedAsync(ct);
        await sp.GetRequiredService<ShopHub.Infrastructure.Seed.LoadUserSeeder>().SeedAsync(ct);
        // Sample orders go through the real order commands: last, once products, vouchers and fees exist
        if (settings.SeedSampleData) await sp.GetRequiredService<ShopHub.Infrastructure.Seed.OrderSampleSeeder>().SeedAsync(ct);
        if (settings.PerfProducts > 0)
        {
            // The perf catalogue indexes itself straight into Meilisearch: configure the index first
            await sp.GetRequiredService<ShopHub.Infrastructure.Search.MeiliSearchIndexer>().ConfigureAsync(ct);
            await sp.GetRequiredService<ShopHub.Infrastructure.Seed.PerfSeeder>().SeedAsync(ct);
        }
        await sp.GetRequiredService<ShopHub.Application.Abstractions.ICounterRecomputer>().RecomputeAllAsync(ct);

        // The search index is a projection: rebuild it whenever it disagrees with the database. If the engine is
        // down the API still starts (searches fall back to PostgreSQL) and the outbox catches up later.
        try
        {
            await sp.GetRequiredService<ShopHub.Infrastructure.Search.MeiliSearchIndexer>().EnsureFreshAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            logger.LogWarning(ex, "Meilisearch not reachable at startup; search falls back to PostgreSQL");
        }

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

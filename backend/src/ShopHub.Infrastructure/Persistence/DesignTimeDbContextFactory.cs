using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Persistence;

// Used only by `dotnet ef` (migrations add/script); reads the same SH_* variables as the API
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShopHubDbContext>
{
    public ShopHubDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var settings = ShopHubSettings.FromConfiguration(config);
        var options = new DbContextOptionsBuilder<ShopHubDbContext>()
            .UseNpgsql(settings.DbConnectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "sys"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ShopHubDbContext(options);
    }
}

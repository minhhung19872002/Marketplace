using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ShopHub.Infrastructure.Configuration;

// All runtime settings come from SH_* environment variables (see .env.example)
public sealed class ShopHubSettings
{
    public required string DbConnectionString { get; init; }
    public required string RedisUrl { get; init; }
    public required string MinioEndpoint { get; init; }
    public required string MeiliUrl { get; init; }
    public string? MeiliMasterKey { get; init; }
    public bool MigrateOnStartup { get; init; }
    public bool JobsEnabled { get; init; }
    public required string JwtSecret { get; init; }
    public required string SmtpHost { get; init; }
    public int SmtpPort { get; init; }
    public required string SmtpFrom { get; init; }

    // "simulated" (default) writes SMS to sys.simulated_sms; a real provider plugs in behind ISmsSender
    public required string SmsProvider { get; init; }

    // Sample accounts/data for demo (on by default; set SH_SEED_SAMPLE=false for a clean production DB)
    public bool SeedSampleData { get; init; }

    public static ShopHubSettings FromConfiguration(IConfiguration config)
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = config["SH_DB_HOST"] ?? "localhost",
            Port = int.TryParse(config["SH_DB_PORT"], out var port) ? port : 18432,
            Database = config["SH_DB_NAME"] ?? "shophub",
            Username = config["SH_DB_USER"] ?? "shophub",
            Password = config["SH_DB_PASSWORD"],
            // Bounded pool + timeouts so a slow DB cannot exhaust the API
            MaxPoolSize = int.TryParse(config["SH_DB_MAX_POOL"], out var pool) ? pool : 100,
            Timeout = 15,
            CommandTimeout = 30,
        };

        return new ShopHubSettings
        {
            DbConnectionString = config["SH_DB_CONNECTION"] ?? csb.ConnectionString,
            RedisUrl = config["SH_REDIS_URL"] ?? "localhost:18379",
            MinioEndpoint = config["SH_MINIO_ENDPOINT"] ?? "localhost:18900",
            MeiliUrl = config["SH_MEILI_URL"] ?? "http://localhost:18700",
            MeiliMasterKey = config["SH_MEILI_MASTER_KEY"],
            MigrateOnStartup = !string.Equals(config["SH_DB_MIGRATE"], "false", StringComparison.OrdinalIgnoreCase),
            JobsEnabled = !string.Equals(config["SH_JOBS_ENABLED"], "false", StringComparison.OrdinalIgnoreCase),
            JwtSecret = config["SH_JWT_SECRET"] is { Length: >= 32 } secret
                ? secret
                : throw new InvalidOperationException("SH_JWT_SECRET phải có ít nhất 32 ký tự."),
            SmtpHost = config["SH_SMTP_HOST"] ?? "localhost",
            SmtpPort = int.TryParse(config["SH_SMTP_PORT"], out var smtpPort) ? smtpPort : 18125,
            SmtpFrom = config["SH_SMTP_FROM"] ?? "ShopHub <no-reply@shophub.local>",
            SmsProvider = config["SH_SMS_PROVIDER"] ?? "simulated",
            SeedSampleData = !string.Equals(config["SH_SEED_SAMPLE"], "false", StringComparison.OrdinalIgnoreCase),
        };
    }
}

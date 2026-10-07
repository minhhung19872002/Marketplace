using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ShopHub.Infrastructure.Configuration;

// All runtime settings come from SH_* environment variables (see .env.example)
public sealed class ShopHubSettings
{
    public required string DbConnectionString { get; init; }
    public required string RedisUrl { get; init; }
    public required string MinioEndpoint { get; init; }
    public required string MinioAccessKey { get; init; }
    public required string MinioSecretKey { get; init; }

    // Base URL browsers use for stored files (gateway /s3 → MinIO)
    public required string MediaPublicUrl { get; init; }

    // 32-byte AES key for column encryption (bank accounts, ID numbers)
    public required byte[] DataKey { get; init; }
    public required string MeiliUrl { get; init; }
    public string? MeiliMasterKey { get; init; }
    public bool MigrateOnStartup { get; init; }
    public bool JobsEnabled { get; init; }
    // Where the database backups are written (SH_BACKUP_DIR; a volume in Docker)
    public string BackupDirectory { get; init; } = "/backups";
    // First-run admin password chosen by the operator (CI secret); null = generated and printed once
    public string? SeedAdminPassword { get; init; }
    public required string JwtSecret { get; init; }
    public required string SmtpHost { get; init; }
    public int SmtpPort { get; init; }
    public required string SmtpFrom { get; init; }

    // "simulated" (default) writes SMS to sys.simulated_sms; a real provider plugs in behind ISmsSender
    public required string SmsProvider { get; init; }

    // Sample accounts/data for demo (on by default; set SH_SEED_SAMPLE=false for a clean production DB)
    public bool SeedSampleData { get; init; }

    // Load-test buyers (k6, e2e/load): SH_SEED_LOAD_USERS accounts 0970000000… with the password in SH_LOAD_USER_PASSWORD.
    // Off unless both are set; never in production.
    public int LoadUsers { get; init; }
    public string? LoadUserPassword { get; init; }

    // SH_SEED=perf: grow the catalogue to SH_PERF_PRODUCTS (default 1.000.000) products for performance measurement
    public int PerfProducts { get; init; }

    // SimulatedGateway (fake payment page that can mark any payment as paid): demo/test only, off unless set
    public bool PaymentSimulated { get; init; }

    // VNPay / MoMo / GHN / GHTK: each on only when its keys are set
    public ProviderSettings Providers { get; init; } = new();

    // Đăng nhập Google: on only when the OAuth client id is set; the key set URL is overridable for tests
    public string? GoogleClientId { get; init; }
    public string GoogleCertsUrl { get; init; } = "https://www.googleapis.com/oauth2/v3/certs";

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
            MinioAccessKey = config["SH_MINIO_ACCESS_KEY"] ?? "shophub",
            MinioSecretKey = config["SH_MINIO_SECRET_KEY"] ?? string.Empty,
            MediaPublicUrl = config["SH_MEDIA_PUBLIC_URL"] ?? "http://localhost:18000/s3",
            DataKey = ParseDataKey(config["SH_DATA_KEY"]),
            MeiliUrl = config["SH_MEILI_URL"] ?? "http://localhost:18700",
            MeiliMasterKey = config["SH_MEILI_MASTER_KEY"],
            MigrateOnStartup = !string.Equals(config["SH_DB_MIGRATE"], "false", StringComparison.OrdinalIgnoreCase),
            JobsEnabled = !string.Equals(config["SH_JOBS_ENABLED"], "false", StringComparison.OrdinalIgnoreCase),
            BackupDirectory = config["SH_BACKUP_DIR"] ?? Path.Combine(Path.GetTempPath(), "shophub-backups"),
            SeedAdminPassword = config["SH_SEED_ADMIN_PASSWORD"] is { Length: >= 8 } adminPassword ? adminPassword : null,
            JwtSecret = config["SH_JWT_SECRET"] is { Length: >= 32 } secret
                ? secret
                : throw new InvalidOperationException("SH_JWT_SECRET phải có ít nhất 32 ký tự."),
            SmtpHost = config["SH_SMTP_HOST"] ?? "localhost",
            SmtpPort = int.TryParse(config["SH_SMTP_PORT"], out var smtpPort) ? smtpPort : 18125,
            SmtpFrom = config["SH_SMTP_FROM"] ?? "ShopHub <no-reply@shophub.local>",
            SmsProvider = config["SH_SMS_PROVIDER"] ?? "simulated",
            SeedSampleData = !string.Equals(config["SH_SEED_SAMPLE"], "false", StringComparison.OrdinalIgnoreCase),
            PaymentSimulated = string.Equals(config["SH_PAYMENT_SIMULATED"], "true", StringComparison.OrdinalIgnoreCase),
            LoadUsers = int.TryParse(config["SH_SEED_LOAD_USERS"], out var loadUsers) ? Math.Clamp(loadUsers, 0, 10_000) : 0,
            LoadUserPassword = string.IsNullOrWhiteSpace(config["SH_LOAD_USER_PASSWORD"]) ? null : config["SH_LOAD_USER_PASSWORD"],
            Providers = ProviderSettings.FromConfiguration(config),
            GoogleClientId = string.IsNullOrWhiteSpace(config["SH_GOOGLE_CLIENT_ID"]) ? null : config["SH_GOOGLE_CLIENT_ID"]!.Trim(),
            GoogleCertsUrl = config["SH_GOOGLE_CERTS_URL"] ?? "https://www.googleapis.com/oauth2/v3/certs",
            PerfProducts = string.Equals(config["SH_SEED"], "perf", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(config["SH_PERF_PRODUCTS"], out var perf) ? Math.Clamp(perf, 1, 5_000_000) : 1_000_000
                : 0,
        };
    }

    private static byte[] ParseDataKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("Thiếu SH_DATA_KEY (khoá mã hoá 32 byte, base64). Sinh bằng: openssl rand -base64 32");
        var key = Convert.FromBase64String(base64);
        return key.Length == 32 ? key : throw new InvalidOperationException("SH_DATA_KEY phải là 32 byte (base64).");
    }
}

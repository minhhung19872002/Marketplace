using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Health;
using ShopHub.Infrastructure.Identity;
using ShopHub.Infrastructure.Notifications;
using ShopHub.Infrastructure.Seed;
using ShopHub.Infrastructure.Jobs;
using ShopHub.Infrastructure.Media;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;
using ShopHub.Infrastructure.Persistence.Interceptors;
using ShopHub.Infrastructure.Services;
using StackExchange.Redis;

namespace ShopHub.Infrastructure;

public static class DependencyInjection
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, ShopHubSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<ShopHubDbContext>((sp, options) => options
            .UseNpgsql(settings.DbConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "sys"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ShopHubDbContext>());

        services.AddSingleton<ISystemParameters, CachedSystemParameters>();
        services.AddScoped<IOutbox, EfOutbox>();

        // Redis: do not crash the API when Redis is briefly unavailable; readiness reports it instead
        var redisOptions = ConfigurationOptions.Parse(settings.RedisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddHostedService<ParameterChangeSubscriber>();

        services.AddScoped<IOutboxHandler, SystemParameterChangedHandler>();
        services.AddScoped<IOutboxHandler, SmsOutboxHandler>();
        services.AddScoped<IOutboxHandler, EmailOutboxHandler>();
        services.AddScoped<IOutboxHandler, SessionsChangedHandler>();
        services.AddSingleton<ImmediateOutboxDispatcher>();
        services.AddSingleton<IOutboxSignal>(sp => sp.GetRequiredService<ImmediateOutboxDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<ImmediateOutboxDispatcher>());
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ISmsSender>(sp => settings.SmsProvider switch
        {
            "simulated" => ActivatorUtilities.CreateInstance<SimulatedSmsSender>(sp),
            _ => throw new InvalidOperationException($"Nhà cung cấp SMS '{settings.SmsProvider}' chưa được hỗ trợ."),
        });
        services.AddHostedService<SessionsChangedSubscriber>();
        services.AddScoped<IOutboxHandler, ShopEventHandler>();
        services.AddScoped<IOutboxHandler, ProductEventHandler>();

        // Media & catalog
        services.AddSingleton<MinioObjectStorage>();
        services.AddSingleton<IObjectStorage>(sp => sp.GetRequiredService<MinioObjectStorage>());
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IVideoInspector, Mp4VideoInspector>();
        services.AddSingleton<Application.Abstractions.IHtmlSanitizer, HtmlSanitizerAdapter>();
        services.AddSingleton<IDataEncryptor, AesGcmDataEncryptor>();
        services.AddScoped<CatalogSeeder>();

        // Identity
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISessionValidator, CachedSessionValidator>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<OutboxCleanupJob>();

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(settings.DbConnectionString), new PostgreSqlStorageOptions
            {
                SchemaName = "hangfire",
                PrepareSchemaIfNecessary = true,
                // A worker that dies mid-job releases it after this timeout (no stuck "processing" jobs)
                InvisibilityTimeout = TimeSpan.FromMinutes(30),
            }));
        if (settings.JobsEnabled) services.AddHangfireServer(o => o.ServerName = $"shophub-{Environment.MachineName}");
        services.AddScoped<IJobScheduler, HangfireJobScheduler>();

        services.AddHttpClient("health", c => c.Timeout = TimeSpan.FromSeconds(3));
        services.AddHealthChecks()
            .AddNpgSql(settings.DbConnectionString, name: "postgres", tags: [ReadyTag])
            .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: [ReadyTag])
            .AddTypeActivatedCheck<HttpEndpointHealthCheck>("minio", null, [ReadyTag],
                $"http://{settings.MinioEndpoint}/minio/health/ready")
            .AddTypeActivatedCheck<HttpEndpointHealthCheck>("meilisearch", null, [ReadyTag],
                $"{settings.MeiliUrl.TrimEnd('/')}/health");

        return services;
    }
}

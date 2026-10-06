using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Finance;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Health;
using ShopHub.Infrastructure.Identity;
using ShopHub.Infrastructure.Notifications;
using ShopHub.Infrastructure.Seed;
using ShopHub.Infrastructure.Jobs;
using ShopHub.Infrastructure.Media;
using ShopHub.Infrastructure.Search;
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
        services.AddSingleton<SearchSyncInterceptor>();
        services.AddScoped<OutboxInterceptor>();
        services.AddDbContext<ShopHubDbContext>((sp, options) => options
            .UseNpgsql(settings.DbConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "sys"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(), sp.GetRequiredService<SearchSyncInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>()));
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
        services.AddScoped<ProductGenerator>();

        // Search (Meilisearch with PostgreSQL fallback) and denormalised counters
        services.AddHttpClient<MeiliClient>();
        services.AddScoped<MeiliProductSearch>();
        services.AddScoped<PostgresProductSearch>();
        services.AddScoped<IProductSearch, ResilientProductSearch>();
        services.AddScoped<MeiliSearchIndexer>();
        services.AddScoped<ISearchIndexer>(sp => sp.GetRequiredService<MeiliSearchIndexer>());
        services.AddScoped<SqlCounterRecomputer>();
        services.AddScoped<ICounterRecomputer>(sp => sp.GetRequiredService<SqlCounterRecomputer>());
        services.AddScoped<IOutboxHandler, SearchSyncProductsHandler>();
        services.AddScoped<IOutboxHandler, SearchSyncShopHandler>();

        // Identity
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISessionValidator, CachedSessionValidator>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<OutboxCleanupJob>();

        // Commerce: carriers and payment gateways behind interfaces; the real ones are on only when their keys are set
        services.AddScoped<Commerce.SimulatedCarrier>();
        services.AddScoped<ICarrier>(sp => sp.GetRequiredService<Commerce.SimulatedCarrier>());
        AddRealProviders(services, settings.Providers);
        services.AddScoped<Commerce.CarrierSimulator>();
        services.AddScoped<IOutboxHandler, OrderEventHandler>();
        services.AddScoped<IOutboxHandler, NotificationDeliveryHandler>();
        services.AddScoped<IPushSender, SimulatedPushSender>();
        services.AddScoped<Jobs.RemindersJob>();
        services.AddScoped<Jobs.CarrierSyncJob>();
        services.AddScoped<Jobs.OrderAutomationJob>();
        services.AddScoped<Jobs.CarrierSimulatorJob>();
        services.AddScoped<Commerce.SimulatedGateway>();
        if (settings.PaymentSimulated)
        {
            services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<Commerce.SimulatedGateway>());
            services.AddScoped<Commerce.SimulatedGatewayDesk>();
        }
        services.AddScoped<IPaymentGatewayRegistry, Commerce.PaymentGatewayRegistry>();
        services.AddScoped<Jobs.PaymentExpiryJob>();
        services.AddScoped<CommerceSeeder>();

        // Finance: ledger sync on order events, simulated bank payouts, release and ledger-check jobs
        services.AddScoped<IOutboxHandler, Finance.FinanceOrderEventHandler>();
        services.AddScoped<IBankPayout, Finance.SimulatedBankPayout>();
        services.AddScoped<Finance.SettlementJob>();
        services.AddScoped<Finance.LedgerCheckJob>();
        services.AddScoped<FinanceSeeder>();
        services.AddScoped<IProviderStatements, Finance.SimulatedProviderStatements>();

        // Marketing: Flash Sale quota counters on Redis, reconciled from PostgreSQL
        services.AddScoped<IFlashSaleCounter, Marketing.RedisFlashSaleCounter>();
        services.AddScoped<Marketing.FlashReconcileJob>();
        services.AddScoped<IOutboxHandler, Marketing.CashbackOrderEventHandler>();
        services.AddScoped<Marketing.CoinExpiryJob>();
        services.AddScoped<MarketingSeeder>();
        services.AddScoped<ContentSeeder>();
        services.AddScoped<LoadUserSeeder>();

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

    /// <summary>VNPay / MoMo / GHN / GHTK (sandbox or production endpoints), each only when its keys are configured.</summary>
    private static void AddRealProviders(IServiceCollection services, ProviderSettings p)
    {
        services.AddSingleton(p);
        services.AddMemoryCache();
        services.AddScoped<Commerce.Providers.DivisionNameResolver>();
        if (p.VnPay is { } vnpay)
        {
            services.AddSingleton(vnpay);
            services.AddHttpClient(Commerce.Providers.VnPayGateway.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
            services.AddScoped<IPaymentGateway, Commerce.Providers.VnPayGateway>();
        }
        if (p.MoMo is { } momo)
        {
            services.AddSingleton(momo);
            services.AddHttpClient(Commerce.Providers.MoMoGateway.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddScoped<IPaymentGateway, Commerce.Providers.MoMoGateway>();
        }
        if (p.Ghn is { } ghn)
        {
            services.AddSingleton(ghn);
            services.AddHttpClient(Commerce.Providers.GhnCarrier.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddScoped<ICarrier, Commerce.Providers.GhnCarrier>();
        }
        if (p.Ghtk is { } ghtk)
        {
            services.AddSingleton(ghtk);
            services.AddHttpClient(Commerce.Providers.GhtkCarrier.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddScoped<ICarrier, Commerce.Providers.GhtkCarrier>();
        }
    }
}

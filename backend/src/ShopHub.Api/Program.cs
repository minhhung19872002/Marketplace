using System.Globalization;
using Hangfire;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
using ShopHub.Api.ErrorHandling;
using ShopHub.Api.Hosting;
using ShopHub.Api.Middleware;
using ShopHub.Application;
using ShopHub.Infrastructure;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;
using ShopHub.Infrastructure.Logging;

// Default process culture is vi-VN (money/date formatting on the server side)
var culture = new CultureInfo("vi-VN");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// Serilog's own failures (e.g. a sink that cannot write) — opt-in diagnostics
if (builder.Configuration["SH_SERILOG_SELFLOG"] is { Length: > 0 } selfLogPath)
{
    var selfLog = TextWriter.Synchronized(File.AppendText(selfLogPath));
    Serilog.Debugging.SelfLog.Enable(msg => { selfLog.WriteLine(msg); selfLog.Flush(); });
}
var settings = ShopHubSettings.FromConfiguration(builder.Configuration);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(ctx.Configuration["SH_LOG_DIR"] ?? "logs", "shophub-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    // Warnings and errors also go to PostgreSQL (sys.logs) for the admin log viewer
    .WriteTo.PostgreSQL(settings.DbConnectionString, "logs", LogTableColumns.Writers, schemaName: "sys",
        needAutoCreateTable: false, restrictedToMinimumLevel: LogEventLevel.Warning,
        // COPY mode calls an Npgsql API whose signature changed in Npgsql 8 (MissingMethodException); INSERT works
        useCopy: false));

builder.Services
    .AddApplication()
    .AddInfrastructure(settings)
    .AddSingleton<ShopHub.Application.Abstractions.IShippingDocuments, ShopHub.Reporting.ShippingDocuments>()
    .AddSingleton<ShopHub.Application.Abstractions.IFinanceDocuments, ShopHub.Reporting.FinanceDocuments>()
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
// Outside the exception handler: the request log shows the status actually sent (400 / 409…), not 500 for every exception
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages(StatusCodeEnvelope.WriteAsync);
app.UseMiddleware<NullCharacterMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
// After authentication so per-user policies (checkout, upload) partition by the token's subject, not one shared bucket
app.UseRateLimiter();
app.UseMiddleware<PasswordChangeGateMiddleware>();
app.UseAuthorization();

// Liveness: process is up. Readiness: DB, Redis, MinIO, Meilisearch reachable.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains(ShopHub.Infrastructure.DependencyInjection.ReadyTag),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
}).AllowAnonymous();

app.MapHangfireDashboard("/api/admin/jobs", new DashboardOptions
{
    Authorization = [new JobDashboardAuthorizationFilter()],
    DashboardTitle = "ShopHub — Việc nền",
}).AllowAnonymous();

app.MapControllers();

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

// Exposed for WebApplicationFactory in integration tests
public partial class Program;

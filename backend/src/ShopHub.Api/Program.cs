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
    .AddSingleton<ShopHub.Application.Abstractions.IReportDocuments, ShopHub.Reporting.ReportDocuments>()
    .AddSingleton<ShopHub.Application.Abstractions.IProductSheets, ShopHub.Reporting.ProductSheets>()
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
app.UseOutputCache();

// Liveness: process is up. Readiness: DB, Redis, MinIO, Meilisearch reachable — publicly only the overall word
// (Healthy / Unhealthy, 200 / 503); the per-dependency report (error messages, hosts) only inside the private network:
// the gateway never routes /health/ready/details (L077)
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains(ShopHub.Infrastructure.DependencyInjection.ReadyTag),
}).AllowAnonymous();
app.MapGet("/health/ready/details", async (HttpContext http, Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService health) =>
{
    if (!ShopHub.Api.Security.NetworkScope.IsInternal(http)) return Results.NotFound();
    var report = await health.CheckHealthAsync(c => c.Tags.Contains(ShopHub.Infrastructure.DependencyInjection.ReadyTag), http.RequestAborted);
    http.Response.StatusCode = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy ? 503 : 200;
    await UIResponseWriter.WriteHealthCheckUIResponse(http, report);
    return Results.Empty;
}).AllowAnonymous().ExcludeFromDescription();

// Browser access to the dashboard: ?ticket= (one use) → httpOnly cookie on /api/admin/jobs (L078)
app.UseMiddleware<ShopHub.Api.Security.JobDashboardTicketMiddleware>();
app.MapHangfireDashboard(ShopHub.Api.Security.JobDashboardAccess.Path, new DashboardOptions
{
    AsyncAuthorization = [new ShopHub.Api.Security.JobDashboardAuthorizationFilter()],
    DashboardTitle = "ShopHub — Việc nền",
}).AllowAnonymous();

app.MapControllers();
// A connection ends with its access token too (L128); the client reconnects with a fresh one
app.MapHub<ShopHub.Api.Hubs.RealtimeHub>("/hubs/realtime", o => o.CloseOnAuthenticationExpiration = true);

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

// Exposed for WebApplicationFactory in integration tests
public partial class Program;

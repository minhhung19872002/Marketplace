using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ShopHub.Api.Common;
using ShopHub.Api.ErrorHandling;
using ShopHub.Api.Security;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Infrastructure.Identity;

namespace ShopHub.Api.Hosting;

public static class ApiServiceExtensions
{
    public const string AuthRateLimit = "auth";
    public const string OtpRateLimit = "otp";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddControllers(o =>
            {
                ModelStateResponse.UseVietnameseMessages(o.ModelBindingMessageProvider);
                // Required-ness is FluentValidation's job (Vietnamese messages), not MVC's implicit [Required]
                o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ModelStateResponse.Create);

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        AddJwt(services, config);
        AddRateLimiting(services, config);
        AddForwardedHeaders(services, config);
        AddSwagger(services);
        return services;
    }

    private static void AddJwt(IServiceCollection services, IConfiguration config)
    {
        var secret = config["SH_JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("SH_JWT_SECRET phải có ít nhất 32 ký tự.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = JwtAccessTokenIssuer.Issuer,
                    ValidAudience = JwtAccessTokenIssuer.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };
                o.Events = new JwtBearerEvents
                {
                    // A valid signature is not enough: the account must still be active and the session still open
                    OnTokenValidated = async ctx =>
                    {
                        var validator = ctx.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
                        var ok = Guid.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var userId)
                                 & Guid.TryParse(ctx.Principal?.FindFirst("sid")?.Value, out var sessionId)
                                 && await validator.IsValidAsync(userId, sessionId, ctx.HttpContext.RequestAborted);
                        if (!ok) ctx.Fail("Phiên đăng nhập không còn hiệu lực.");
                    },
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        // No fallback policy (it would turn unknown routes into 401): every endpoint must declare
        // [RequirePermission] / [AllowAnonymous] / [OwnerGuarded] — enforced by EndpointAuthorisationTests.
        services.AddAuthorization();
    }

    // Per-IP limits for sign-in and OTP; SH_RATE_LIMIT_AUTH / SH_RATE_LIMIT_OTP raise them for test/demo stacks
    // where every client shares one IP
    private static int AuthPermitsPerMinute = 20;
    private static int OtpPermitsPerMinute = 10;

    private static void AddRateLimiting(IServiceCollection services, IConfiguration config)
    {
        if (int.TryParse(config["SH_RATE_LIMIT_AUTH"], out var auth) && auth > 0) AuthPermitsPerMinute = auth;
        if (int.TryParse(config["SH_RATE_LIMIT_OTP"], out var otp) && otp > 0) OtpPermitsPerMinute = otp;

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Fail("Bạn thao tác quá nhanh, vui lòng thử lại sau giây lát."), ct);
            };
            // Sign-in attempts and OTP requests per IP (per-target limits for OTP live in OtpService)
            o.AddPolicy(AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = AuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy(OtpRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = OtpPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));

            // Coarse per-IP ceiling; tighter named policies (login, OTP, checkout…) are added per feature
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
        });
    }

    private static void AddForwardedHeaders(IServiceCollection services, IConfiguration config)
    {
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            // Only these proxy ranges may set X-Forwarded-For; everyone else's header is ignored
            foreach (var cidr in (config["SH_TRUSTED_PROXIES"] ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = cidr.Split('/');
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
                    IPAddress.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 32));
            }
        });
    }

    private static void AddSwagger(IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ShopHub API",
                Version = "v1",
                Description = "API sàn TMĐT ShopHub. Mọi phản hồi theo khuôn { success, data, message, errors }.",
            });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token (15 phút).",
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
            });
            o.CustomSchemaIds(t => t.FullName?.Replace('+', '.'));
            var xml = Path.Combine(AppContext.BaseDirectory, "ShopHub.Api.xml");
            if (File.Exists(xml)) o.IncludeXmlComments(xml);
        });
    }
}

// Hangfire dashboard is only for platform admins holding SYS.JOB.VIEW
public sealed class JobDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        PermissionClaims.Has(context.GetHttpContext().User, Permissions.JobDashboardView);
}

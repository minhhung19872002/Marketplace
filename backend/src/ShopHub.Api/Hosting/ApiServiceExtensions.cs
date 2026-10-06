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

namespace ShopHub.Api.Hosting;

public static class ApiServiceExtensions
{
    public const string JwtIssuer = "ShopHub";
    public const string JwtAudience = "ShopHub";

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
        AddRateLimiting(services);
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
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        // No fallback policy (it would turn unknown routes into 401): every endpoint must declare
        // [RequirePermission] / [AllowAnonymous] / [OwnerGuarded] — enforced by EndpointAuthorisationTests.
        services.AddAuthorization();
    }

    private static void AddRateLimiting(IServiceCollection services)
    {
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

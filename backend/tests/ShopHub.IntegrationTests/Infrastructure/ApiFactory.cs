using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShopHub.Application.Security;
using ShopHub.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ShopHub.IntegrationTests.Infrastructure;

/// <summary>
/// Real PostgreSQL 16 + Redis 7 in containers, the real API pipeline (auth, middleware, EF interceptors) on top.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtSecret = "integration-test-secret-key-at-least-32-chars!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("shophub")
        .WithUsername("shophub")
        .WithPassword("shophub-test")
        .Build();

    private readonly IContainer _redis = new ContainerBuilder()
        .WithImage("redis:7-alpine")
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public string RedisEndpoint => $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Program reads SH_* before the host is built, so they are provided as environment variables
        Environment.SetEnvironmentVariable("SH_DB_CONNECTION", ConnectionString);
        Environment.SetEnvironmentVariable("SH_REDIS_URL", RedisEndpoint);
        Environment.SetEnvironmentVariable("SH_JWT_SECRET", JwtSecret);
        Environment.SetEnvironmentVariable("SH_JOBS_ENABLED", "false");
        Environment.SetEnvironmentVariable("SH_MINIO_ENDPOINT", "127.0.0.1:1");
        Environment.SetEnvironmentVariable("SH_MEILI_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("SH_LOG_DIR", Path.Combine(Path.GetTempPath(), "shophub-it-logs"));

        // Force host start (migrations + seed) before tests run
        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    public HttpClient ClientWithPermissions(params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(Guid.NewGuid(), permissions));
        return client;
    }

    public static string IssueToken(Guid userId, IEnumerable<string> permissions, TimeSpan? lifetime = null)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()) };
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        var token = new JwtSecurityToken(
            issuer: "ShopHub",
            audience: "ShopHub",
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task WithDbAsync(Func<ShopHubDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ShopHubDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<ShopHubDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ShopHubDbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

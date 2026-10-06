using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ShopHub.Infrastructure.Health;

// Readiness probe for HTTP services that expose their own health URL (MinIO, Meilisearch)
public sealed class HttpEndpointHealthCheck(IHttpClientFactory httpClientFactory, string url) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("health");
            using var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}

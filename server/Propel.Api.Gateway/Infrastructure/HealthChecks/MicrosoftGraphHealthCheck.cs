using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for the Microsoft Graph API (EP-011/us_052, NFR-003, NFR-018).
/// Sends an HTTP GET to the Graph OData <c>$metadata</c> endpoint, which is publicly accessible
/// and returns EDMX metadata without authentication — a lightweight reachability probe.
/// Returns <see cref="HealthCheckResult.Degraded"/> (never <see cref="HealthCheckResult.Unhealthy"/>)
/// on failure; Microsoft Graph (Outlook Calendar) is non-critical to core workflows (AG-6, NFR-018).
/// </summary>
internal sealed class MicrosoftGraphHealthCheck : IHealthCheck
{
    // The $metadata endpoint is publicly accessible (no auth required) and returns OData EDMX.
    private const string GraphMetadataUrl = "https://graph.microsoft.com/v1.0/$metadata";

    private readonly IHttpClientFactory _httpClientFactory;

    public MicrosoftGraphHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client   = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(GraphMetadataUrl, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Microsoft Graph API reachable")
                : HealthCheckResult.Degraded($"Microsoft Graph returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Microsoft Graph API unreachable", ex);
        }
    }
}

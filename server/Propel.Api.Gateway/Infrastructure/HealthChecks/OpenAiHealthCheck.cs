using System.Net.Http.Headers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for the OpenAI API (EP-011/us_052, NFR-003, NFR-018).
/// Sends an HTTP GET to the OpenAI models list endpoint — lightweight, read-only, no tokens consumed.
/// Returns <see cref="HealthCheckResult.Degraded"/> on failure; AI features are non-critical
/// to core booking and clinical workflows (AG-6, NFR-018).
/// API key is read from the <c>OPENAI_API_KEY</c> environment variable — never from config
/// files or hardcoded values (OWASP A02).
/// The health response JSON exposes only status and description; no credential details
/// are included (OWASP A09).
/// </summary>
internal sealed class OpenAiHealthCheck : IHealthCheck
{
    private const string OpenAiModelsUrl = "https://api.openai.com/v1/models";

    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAiHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // API key lives exclusively in environment variables — never in config files (OWASP A02).
        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return HealthCheckResult.Degraded("OPENAI_API_KEY not configured");

        try
        {
            var client  = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, OpenAiModelsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("OpenAI reachable")
                : HealthCheckResult.Degraded($"OpenAI returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("OpenAI unreachable", ex);
        }
    }
}

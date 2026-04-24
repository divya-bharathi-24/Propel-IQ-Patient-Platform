using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for the SendGrid email API (EP-011/us_052, NFR-003, NFR-018).
/// Sends an HTTP HEAD to the SendGrid scopes endpoint — no email is sent.
/// Returns <see cref="HealthCheckResult.Degraded"/> on failure; email is non-critical to
/// core booking and clinical workflows (AG-6, NFR-018).
/// API key is read from <c>SendGrid:ApiKey</c> configuration — never hardcoded (OWASP A02).
/// The health response JSON exposes only status and description; no credential or host
/// details are included (OWASP A09).
/// </summary>
internal sealed class SendGridHealthCheck : IHealthCheck
{
    private const string SendGridScopesUrl = "https://api.sendgrid.com/v3/scopes";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SendGridHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return HealthCheckResult.Degraded("SendGrid:ApiKey not configured");

        try
        {
            var client  = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Head, SendGridScopesUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("SendGrid reachable")
                : HealthCheckResult.Degraded($"SendGrid returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("SendGrid unreachable", ex);
        }
    }
}

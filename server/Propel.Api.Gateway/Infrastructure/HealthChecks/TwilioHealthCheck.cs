using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for the Twilio SMS API (EP-011/us_052, NFR-003, NFR-018).
/// Sends an HTTP HEAD to the Twilio Accounts endpoint with Basic auth — no SMS is sent.
/// Returns <see cref="HealthCheckResult.Degraded"/> on failure; SMS is non-critical to
/// core booking and clinical workflows (AG-6, NFR-018).
/// Credentials are read from <c>Twilio:AccountSid</c> and <c>Twilio:AuthToken</c>
/// configuration — never hardcoded (OWASP A02).
/// The health response JSON exposes only status and description; no credential or host
/// details are included (OWASP A09).
/// </summary>
internal sealed class TwilioHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public TwilioHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        string? accountSid = _configuration["Twilio:AccountSid"];
        string? authToken  = _configuration["Twilio:AuthToken"];

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken))
            return HealthCheckResult.Degraded("Twilio credentials not configured");

        try
        {
            var client  = _httpClientFactory.CreateClient();
            var url     = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}";
            var request = new HttpRequestMessage(HttpMethod.Head, url);

            // Basic auth: Base64-encoded "AccountSid:AuthToken"
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Twilio reachable")
                : HealthCheckResult.Degraded($"Twilio returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Twilio unreachable", ex);
        }
    }
}

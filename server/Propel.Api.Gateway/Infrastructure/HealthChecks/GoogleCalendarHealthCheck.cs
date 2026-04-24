using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for the Google Calendar API (EP-011/us_052, NFR-003, NFR-018).
/// Sends an unauthenticated HTTP GET to the Google Calendar API endpoint.
/// A 401 Unauthorized response is treated as reachable — it confirms the API is up and
/// responding, as no user-level OAuth token is held at the platform configuration level.
/// Returns <see cref="HealthCheckResult.Degraded"/> (never <see cref="HealthCheckResult.Unhealthy"/>)
/// on failure; Google Calendar is non-critical to core booking and clinical workflows (AG-6, NFR-018).
/// </summary>
internal sealed class GoogleCalendarHealthCheck : IHealthCheck
{
    // An unauthenticated request returns 401 when the API is up — treated as reachable.
    private const string GoogleCalendarApiUrl =
        "https://www.googleapis.com/calendar/v3/users/me/calendarList";

    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleCalendarHealthCheck(IHttpClientFactory httpClientFactory)
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
            var request  = new HttpRequestMessage(HttpMethod.Get, GoogleCalendarApiUrl);
            var response = await client.SendAsync(request, cancellationToken);

            // Any non-5xx response (including 401 Unauthorized) confirms the API is reachable.
            bool isReachable = (int)response.StatusCode < 500;
            return isReachable
                ? HealthCheckResult.Healthy("Google Calendar API reachable")
                : HealthCheckResult.Degraded($"Google Calendar API returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Google Calendar API unreachable", ex);
        }
    }
}

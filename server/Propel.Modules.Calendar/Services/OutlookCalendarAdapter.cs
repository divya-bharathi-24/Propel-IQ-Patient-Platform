using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using Propel.Domain.Entities;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Microsoft Graph API v1.0 adapter for PATCH (update) and DELETE operations triggered by
/// appointment reschedule and cancel flows (us_037, AC-1, AC-2).
/// <para>
/// Receives a pre-validated plaintext <c>accessToken</c> from <c>CalendarPropagationService</c>.
/// Does <b>not</b> perform token refresh — returns <see cref="CalendarApiResult.Unauthorized"/>
/// on HTTP 401 so the orchestrator can trigger a silent refresh via <see cref="IOAuthTokenService"/>
/// and retry once (EC-1).
/// </para>
/// </summary>
public sealed class OutlookCalendarAdapter : IOutlookCalendarAdapter
{
    private readonly ILogger<OutlookCalendarAdapter> _logger;

    public OutlookCalendarAdapter(ILogger<OutlookCalendarAdapter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CalendarApiResult> UpdateEventAsync(
        string externalEventId,
        Appointment appointment,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var graphClient = BuildGraphClient(accessToken);
            var eventPatch  = BuildEventPatch(appointment);

            await graphClient.Me.Events[externalEventId]
                .PatchAsync(eventPatch, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "OutlookCalendarAdapter: PATCH succeeded for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                externalEventId, appointment.Id);

            return CalendarApiResult.Synced();
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "OutlookCalendarAdapter: PATCH returned 401 for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                externalEventId, appointment.Id);
            return CalendarApiResult.Unauthorized(ex.ResponseStatusCode);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "OutlookCalendarAdapter: PATCH failed with HTTP {Status} for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                ex.ResponseStatusCode, externalEventId, appointment.Id);
            return CalendarApiResult.Failure(
                ex.Error?.Message ?? ex.Message, ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OutlookCalendarAdapter: PATCH threw unexpected exception for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                externalEventId, appointment.Id);
            return CalendarApiResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<CalendarApiResult> DeleteEventAsync(
        string externalEventId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var graphClient = BuildGraphClient(accessToken);

            await graphClient.Me.Events[externalEventId]
                .DeleteAsync(cancellationToken: cancellationToken);

            _logger.LogInformation(
                "OutlookCalendarAdapter: DELETE succeeded for ExternalEventId={ExternalEventId}",
                externalEventId);

            return CalendarApiResult.Revoked();
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "OutlookCalendarAdapter: DELETE returned 401 for ExternalEventId={ExternalEventId}",
                externalEventId);
            return CalendarApiResult.Unauthorized(ex.ResponseStatusCode);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "OutlookCalendarAdapter: DELETE failed with HTTP {Status} for ExternalEventId={ExternalEventId}",
                ex.ResponseStatusCode, externalEventId);
            return CalendarApiResult.Failure(
                ex.Error?.Message ?? ex.Message, ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OutlookCalendarAdapter: DELETE threw unexpected exception for ExternalEventId={ExternalEventId}",
                externalEventId);
            return CalendarApiResult.Failure(ex.Message);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static GraphServiceClient BuildGraphClient(string accessToken)
    {
        var tokenProvider = new StaticAccessTokenProvider(accessToken);
        var authProvider  = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }

    private static Event BuildEventPatch(Appointment appointment)
    {
        var startDt = appointment.Date.ToDateTime(
            appointment.TimeSlotStart ?? TimeOnly.MinValue);

        var endDt = appointment.Date.ToDateTime(
            appointment.TimeSlotEnd
                ?? (appointment.TimeSlotStart?.AddHours(1) ?? TimeOnly.MinValue.AddHours(1)));

        return new Event
        {
            Subject = $"Appointment — {appointment.Specialty?.Name ?? "Unknown"}",
            Start   = new DateTimeTimeZone
            {
                DateTime = startDt.ToString("o"),
                TimeZone = "UTC"
            },
            End = new DateTimeTimeZone
            {
                DateTime = endDt.ToString("o"),
                TimeZone = "UTC"
            }
        };
    }

    // ── Inner class: static token provider for Microsoft.Graph v5 ─────────────
    private sealed class StaticAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string _token;
        public StaticAccessTokenProvider(string token) => _token = token;
        public AllowedHostsValidator AllowedHostsValidator { get; } = new();
        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_token);
    }
}

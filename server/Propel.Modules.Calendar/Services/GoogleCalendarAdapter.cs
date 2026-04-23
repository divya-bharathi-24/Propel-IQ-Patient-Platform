using System.Net;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Google Calendar API v3 adapter for PATCH (update) and DELETE operations triggered by
/// appointment reschedule and cancel flows (us_037, AC-1, AC-2).
/// <para>
/// Receives a pre-validated plaintext <c>accessToken</c> from <c>CalendarPropagationService</c>.
/// Does <b>not</b> perform token refresh — returns <see cref="CalendarApiResult.Unauthorized"/>
/// on HTTP 401 so the orchestrator can trigger a silent refresh via <see cref="IOAuthTokenService"/>
/// and retry once (EC-1).
/// </para>
/// </summary>
public sealed class GoogleCalendarAdapter : IGoogleCalendarAdapter
{
    private readonly ILogger<GoogleCalendarAdapter> _logger;

    public GoogleCalendarAdapter(ILogger<GoogleCalendarAdapter> logger)
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
            var service   = BuildCalendarService(accessToken);
            var eventBody = BuildEvent(appointment);

            await service.Events
                .Patch(eventBody, "primary", externalEventId)
                .ExecuteAsync(cancellationToken);

            _logger.LogInformation(
                "GoogleCalendarAdapter: PATCH succeeded for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                externalEventId, appointment.Id);

            return CalendarApiResult.Synced();
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "GoogleCalendarAdapter: PATCH returned 401 for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                externalEventId, appointment.Id);
            return CalendarApiResult.Unauthorized((int)ex.HttpStatusCode);
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogError(ex,
                "GoogleCalendarAdapter: PATCH failed with HTTP {Status} for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
                ex.HttpStatusCode, externalEventId, appointment.Id);
            return CalendarApiResult.Failure(ex.Message, (int)ex.HttpStatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GoogleCalendarAdapter: PATCH threw unexpected exception for ExternalEventId={ExternalEventId} AppointmentId={AppointmentId}",
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
            var service = BuildCalendarService(accessToken);

            await service.Events
                .Delete("primary", externalEventId)
                .ExecuteAsync(cancellationToken);

            _logger.LogInformation(
                "GoogleCalendarAdapter: DELETE succeeded for ExternalEventId={ExternalEventId}",
                externalEventId);

            return CalendarApiResult.Revoked();
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "GoogleCalendarAdapter: DELETE returned 401 for ExternalEventId={ExternalEventId}",
                externalEventId);
            return CalendarApiResult.Unauthorized((int)ex.HttpStatusCode);
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogError(ex,
                "GoogleCalendarAdapter: DELETE failed with HTTP {Status} for ExternalEventId={ExternalEventId}",
                ex.HttpStatusCode, externalEventId);
            return CalendarApiResult.Failure(ex.Message, (int)ex.HttpStatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GoogleCalendarAdapter: DELETE threw unexpected exception for ExternalEventId={ExternalEventId}",
                externalEventId);
            return CalendarApiResult.Failure(ex.Message);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static CalendarService BuildCalendarService(string accessToken)
    {
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "PropelIQ"
        });
    }

    private static Event BuildEvent(Appointment appointment)
    {
        var specialtyName = appointment.Specialty?.Name ?? "Unknown Specialty";
        const string clinicName = "PropelIQ Clinic";

        var start = appointment.TimeSlotStart.HasValue
            ? appointment.Date.ToDateTime(appointment.TimeSlotStart.Value, DateTimeKind.Utc)
            : appointment.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var end = appointment.TimeSlotEnd.HasValue
            ? appointment.Date.ToDateTime(appointment.TimeSlotEnd.Value, DateTimeKind.Utc)
            : start.AddHours(1);

        return new Event
        {
            Summary     = $"Appointment: General — {specialtyName}",
            Location    = clinicName,
            Description = $"Provider: {specialtyName}\nBooking Ref: {appointment.Id}\nClinic: {clinicName}",
            Start       = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = "UTC" },
            End         = new EventDateTime { DateTimeDateTimeOffset = end,   TimeZone = "UTC" }
        };
    }
}

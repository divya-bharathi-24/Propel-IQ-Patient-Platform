using System.Net;
using System.Net.Http.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Domain.Entities;
using Propel.Modules.Calendar.Exceptions;
using Propel.Modules.Calendar.Interfaces;
using Propel.Modules.Calendar.Options;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Wraps Google Calendar API v3 for event create/update/delete (us_035, AC-2).
/// <list type="bullet">
///   <item>Tokens are decrypted via <see cref="IDataProtectionProvider"/> before use.</item>
///   <item>On HTTP 401 from Google, calls <see cref="RefreshTokenAsync"/> and retries once.</item>
///   <item>If refresh also returns 401/400, throws <see cref="GoogleTokenExpiredException"/>.</item>
/// </list>
/// </summary>
public sealed class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<GoogleCalendarService> _logger;
    private readonly GoogleCalendarSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DataProtectionPurpose = "GoogleOAuthTokens.v1";

    public GoogleCalendarService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<GoogleCalendarSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleCalendarService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(string ExternalEventId, string EventLink)> CreateOrUpdateEventAsync(
        Appointment appointment,
        PatientOAuthToken token,
        string? existingExternalEventId,
        CancellationToken cancellationToken)
    {
        var accessToken = _protector.Unprotect(token.EncryptedAccessToken);
        var service = BuildCalendarService(accessToken);
        var eventBody = BuildEvent(appointment);

        try
        {
            return await ExecuteCreateOrUpdateAsync(service, eventBody, existingExternalEventId, cancellationToken);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "Google Calendar 401 for PatientId={PatientId} — attempting token refresh",
                token.PatientId);

            accessToken = await RefreshTokenAsync(token, cancellationToken);
            service = BuildCalendarService(accessToken);

            try
            {
                return await ExecuteCreateOrUpdateAsync(service, eventBody, existingExternalEventId, cancellationToken);
            }
            catch (Google.GoogleApiException retryEx) when (retryEx.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogError(
                    "Google Calendar token refresh still yields 401 for PatientId={PatientId}. Marking Revoked.",
                    token.PatientId);
                throw new GoogleTokenExpiredException(
                    "Refreshed access token was rejected by Google — token pair is invalid.", retryEx);
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteEventAsync(
        PatientOAuthToken token,
        string externalEventId,
        CancellationToken cancellationToken)
    {
        var accessToken = _protector.Unprotect(token.EncryptedAccessToken);
        var service = BuildCalendarService(accessToken);

        try
        {
            await service.Events.Delete("primary", externalEventId)
                .ExecuteAsync(cancellationToken);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            accessToken = await RefreshTokenAsync(token, cancellationToken);
            service = BuildCalendarService(accessToken);

            try
            {
                await service.Events.Delete("primary", externalEventId)
                    .ExecuteAsync(cancellationToken);
            }
            catch (Google.GoogleApiException retryEx) when (retryEx.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                throw new GoogleTokenExpiredException(
                    "Refreshed access token rejected by Google on delete attempt.", retryEx);
            }
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static CalendarService BuildCalendarService(string accessToken)
    {
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "PropelIQ"
        });
    }

    private static Event BuildEvent(Appointment appointment)
    {
        var specialtyName = appointment.Specialty?.Name ?? "Unknown Specialty";
        const string clinicName = "PropelIQ Clinic";

        var start = appointment.TimeSlotStart.HasValue
            ? appointment.Date.ToDateTime(appointment.TimeSlotStart.Value, DateTimeKind.Local)
            : appointment.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);

        var end = appointment.TimeSlotEnd.HasValue
            ? appointment.Date.ToDateTime(appointment.TimeSlotEnd.Value, DateTimeKind.Local)
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

    private static async Task<(string ExternalEventId, string EventLink)> ExecuteCreateOrUpdateAsync(
        CalendarService service,
        Event eventBody,
        string? existingExternalEventId,
        CancellationToken cancellationToken)
    {
        Event created;

        if (!string.IsNullOrEmpty(existingExternalEventId))
        {
            created = await service.Events
                .Patch(eventBody, "primary", existingExternalEventId)
                .ExecuteAsync(cancellationToken);
        }
        else
        {
            created = await service.Events
                .Insert(eventBody, "primary")
                .ExecuteAsync(cancellationToken);
        }

        return (created.Id, created.HtmlLink ?? string.Empty);
    }

    private async Task<string> RefreshTokenAsync(PatientOAuthToken token, CancellationToken cancellationToken)
    {
        var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")
            ?? throw new InvalidOperationException(
                "GOOGLE_CLIENT_SECRET environment variable is not set. Cannot refresh Google token.");

        var refreshToken = _protector.Unprotect(token.EncryptedRefreshToken);

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.PostAsync(
            GoogleTokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = _settings.ClientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"]    = "refresh_token"
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Google token refresh failed with HTTP {Status} for PatientId={PatientId}",
                response.StatusCode, token.PatientId);
            throw new GoogleTokenExpiredException(
                $"Google token refresh endpoint returned {response.StatusCode}.");
        }

        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
            cancellationToken: cancellationToken)
            ?? throw new GoogleTokenExpiredException("Empty response from Google token endpoint.");

        return json.TryGetValue("access_token", out var at)
            ? at.ToString()!
            : throw new GoogleTokenExpiredException("No access_token in Google refresh response.");
    }
}

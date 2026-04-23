using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Exceptions;
using Propel.Modules.Calendar.Interfaces;
using Propel.Modules.Calendar.Options;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="HandleGoogleCallbackCommand"/> for <c>GET /api/calendar/google/callback</c>
/// (us_035, AC-2, AC-3, AC-4).
/// <list type="number">
///   <item>If <c>error=access_denied</c>: redirect declined (AC-3) — no CalendarSync created.</item>
///   <item>Validate and consume PKCE state from <see cref="IOAuthStateService"/> (OWASP A07).</item>
///   <item>Exchange authorization code for tokens at Google's token endpoint.</item>
///   <item>Encrypt tokens via Data Protection API and upsert <c>PatientOAuthToken</c>.</item>
///   <item>Load appointment; call <see cref="IGoogleCalendarService.CreateOrUpdateEventAsync"/>.</item>
///   <item>On success: upsert <c>CalendarSync(Synced)</c>, redirect success.</item>
///   <item>On <see cref="HttpRequestException"/>: upsert <c>CalendarSync(Failed)</c> + retry in 10 min (AC-4).</item>
///   <item>On <see cref="GoogleTokenExpiredException"/>: upsert <c>CalendarSync(Revoked)</c>, redirect expired.</item>
/// </list>
/// </summary>
public sealed class HandleGoogleCallbackCommandHandler
    : IRequestHandler<HandleGoogleCallbackCommand, string>
{
    private readonly IOAuthStateService _stateService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _protector;
    private readonly ICalendarSyncRepository _calendarSyncRepo;
    private readonly IPatientOAuthTokenRepository _oauthTokenRepo;
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly GoogleCalendarSettings _settings;
    private readonly ILogger<HandleGoogleCallbackCommandHandler> _logger;

    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DataProtectionPurpose = "GoogleOAuthTokens.v1";

    public HandleGoogleCallbackCommandHandler(
        IOAuthStateService stateService,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        ICalendarSyncRepository calendarSyncRepo,
        IPatientOAuthTokenRepository oauthTokenRepo,
        IAppointmentBookingRepository appointmentRepo,
        IGoogleCalendarService googleCalendarService,
        IAuditLogRepository auditLogRepo,
        IOptions<GoogleCalendarSettings> settings,
        ILogger<HandleGoogleCallbackCommandHandler> logger)
    {
        _stateService          = stateService;
        _httpClientFactory     = httpClientFactory;
        _protector             = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _calendarSyncRepo      = calendarSyncRepo;
        _oauthTokenRepo        = oauthTokenRepo;
        _appointmentRepo       = appointmentRepo;
        _googleCalendarService = googleCalendarService;
        _auditLogRepo          = auditLogRepo;
        _settings              = settings.Value;
        _logger                = logger;
    }

    public async Task<string> Handle(
        HandleGoogleCallbackCommand request,
        CancellationToken cancellationToken)
    {
        // ── AC-3: Patient denied consent ──────────────────────────────────────
        if (string.Equals(request.Error, "access_denied", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Google OAuth declined by patient (access_denied).");
            return $"{_settings.FrontendConfirmationUrl}?calendarResult=declined";
        }

        // ── OWASP A07: Validate and consume PKCE state (one-time-use) ─────────
        if (string.IsNullOrEmpty(request.State) || string.IsNullOrEmpty(request.Code))
            return $"{_settings.FrontendConfirmationUrl}?calendarResult=failed";

        var payload = await _stateService.GetAndDeleteAsync(request.State, cancellationToken);
        if (payload is null)
        {
            _logger.LogWarning("OAuth state not found or expired: {State}", request.State);
            return $"{_settings.FrontendConfirmationUrl}?calendarResult=failed";
        }

        using var doc = JsonDocument.Parse(payload);
        var root         = doc.RootElement;
        var codeVerifier  = root.GetProperty("codeVerifier").GetString()!;
        var patientId     = Guid.Parse(root.GetProperty("patientId").GetString()!);
        var appointmentId = Guid.Parse(root.GetProperty("appointmentId").GetString()!);

        // ── Exchange authorization code for tokens ────────────────────────────
        var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")
            ?? throw new InvalidOperationException(
                "GOOGLE_CLIENT_SECRET environment variable is not set.");

        var httpClient = _httpClientFactory.CreateClient();
        var tokenResponse = await httpClient.PostAsync(
            GoogleTokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"]          = request.Code,
                ["client_id"]     = _settings.ClientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"]  = _settings.RedirectUri,
                ["grant_type"]    = "authorization_code",
                ["code_verifier"] = codeVerifier
            }),
            cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Google token exchange failed: HTTP {Status} for PatientId={PatientId}",
                tokenResponse.StatusCode, patientId);
            return $"{_settings.FrontendConfirmationUrl}?calendarResult=failed&appointmentId={appointmentId}";
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty response from Google token endpoint.");

        var accessToken  = tokenJson["access_token"].GetString()!;
        var refreshToken = tokenJson.TryGetValue("refresh_token", out var rt) ? rt.GetString()! : "";
        var expiresIn    = tokenJson.TryGetValue("expires_in", out var ei) ? ei.GetInt32() : 3600;

        // ── Encrypt and upsert PatientOAuthToken (NFR-004, OWASP A02) ─────────
        var existingToken = await _oauthTokenRepo.GetAsync(patientId, "Google", cancellationToken);
        var oauthToken = existingToken ?? new PatientOAuthToken
        {
            Id         = Guid.NewGuid(),
            PatientId  = patientId,
            Provider   = "Google",
            CreatedAt  = DateTime.UtcNow
        };

        oauthToken.EncryptedAccessToken  = _protector.Protect(accessToken);
        oauthToken.EncryptedRefreshToken = string.IsNullOrEmpty(refreshToken)
            ? oauthToken.EncryptedRefreshToken  // keep existing refresh token if Google didn't return a new one
            : _protector.Protect(refreshToken);
        oauthToken.ExpiresAt  = DateTime.UtcNow.AddSeconds(expiresIn);
        oauthToken.UpdatedAt  = DateTime.UtcNow;

        await _oauthTokenRepo.UpsertAsync(oauthToken, cancellationToken);

        // ── Load appointment with Specialty ───────────────────────────────────
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(appointmentId, cancellationToken);
        if (appointment is null)
        {
            _logger.LogError("Appointment {AppointmentId} not found for Google Calendar sync.", appointmentId);
            return $"{_settings.FrontendConfirmationUrl}?calendarResult=failed&appointmentId={appointmentId}";
        }

        // ── Check for existing CalendarSync record ────────────────────────────
        var existingSync = await _calendarSyncRepo.GetAsync(
            patientId, appointmentId, CalendarProvider.Google, cancellationToken);

        string? existingExternalEventId = existingSync?.SyncStatus == CalendarSyncStatus.Synced
            ? existingSync.ExternalEventId
            : null;

        // ── Create/Update Google Calendar event ───────────────────────────────
        try
        {
            var (externalEventId, eventLink) = await _googleCalendarService.CreateOrUpdateEventAsync(
                appointment, oauthToken, existingExternalEventId, cancellationToken);

            // ── Upsert CalendarSync: Synced ───────────────────────────────────
            var calendarSync = existingSync ?? new CalendarSync
            {
                Id              = Guid.NewGuid(),
                PatientId       = patientId,
                AppointmentId   = appointmentId,
                Provider        = CalendarProvider.Google,
                ExternalEventId = string.Empty,
                CreatedAt       = DateTime.UtcNow
            };

            calendarSync.ExternalEventId = externalEventId;
            calendarSync.EventLink       = eventLink;
            calendarSync.SyncStatus      = CalendarSyncStatus.Synced;
            calendarSync.SyncedAt        = DateTime.UtcNow;
            calendarSync.ErrorMessage    = null;
            calendarSync.UpdatedAt       = DateTime.UtcNow;

            await _calendarSyncRepo.UpsertAsync(calendarSync, cancellationToken);

            await WriteAuditLogAsync(patientId, appointmentId, "GoogleCalendarSynced", cancellationToken);

            _logger.LogInformation(
                "Google Calendar event created/updated for PatientId={PatientId} AppointmentId={AppointmentId} EventId={EventId}",
                patientId, appointmentId, externalEventId);

            return $"{_settings.FrontendConfirmationUrl}?calendarResult=success&appointmentId={appointmentId}";
        }
        catch (GoogleTokenExpiredException ex)
        {
            _logger.LogError(ex,
                "Google token expired for PatientId={PatientId} AppointmentId={AppointmentId}",
                patientId, appointmentId);

            await UpsertFailedSyncAsync(
                existingSync, patientId, appointmentId, CalendarSyncStatus.Revoked,
                ex.Message, retryScheduledAt: null, cancellationToken);

            await WriteAuditLogAsync(patientId, appointmentId, "GoogleCalendarSyncRevoked", cancellationToken);

            return $"{_settings.FrontendConfirmationUrl}?calendarResult=expired&appointmentId={appointmentId}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Google Calendar API unavailable for PatientId={PatientId} AppointmentId={AppointmentId}",
                patientId, appointmentId);

            await UpsertFailedSyncAsync(
                existingSync, patientId, appointmentId, CalendarSyncStatus.Failed,
                ex.Message, retryScheduledAt: DateTime.UtcNow.AddMinutes(10), cancellationToken);

            await WriteAuditLogAsync(patientId, appointmentId, "GoogleCalendarSyncFailed", cancellationToken);

            return $"{_settings.FrontendConfirmationUrl}?calendarResult=failed&appointmentId={appointmentId}";
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task UpsertFailedSyncAsync(
        CalendarSync? existingSync,
        Guid patientId,
        Guid appointmentId,
        CalendarSyncStatus status,
        string errorMessage,
        DateTime? retryScheduledAt,
        CancellationToken cancellationToken)
    {
        var calendarSync = existingSync ?? new CalendarSync
        {
            Id            = Guid.NewGuid(),
            PatientId     = patientId,
            AppointmentId = appointmentId,
            Provider      = CalendarProvider.Google,
            ExternalEventId = string.Empty,
            CreatedAt     = DateTime.UtcNow
        };

        calendarSync.SyncStatus        = status;
        calendarSync.ErrorMessage      = errorMessage[..Math.Min(errorMessage.Length, 500)];
        calendarSync.RetryScheduledAt  = retryScheduledAt;
        calendarSync.UpdatedAt         = DateTime.UtcNow;

        await _calendarSyncRepo.UpsertAsync(calendarSync, cancellationToken);
    }

    private async Task WriteAuditLogAsync(
        Guid patientId,
        Guid appointmentId,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                UserId     = patientId,
                Action     = action,
                EntityType = "CalendarSync",
                EntityId   = appointmentId,
                Timestamp  = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit failure must never interrupt the main flow
            _logger.LogWarning(ex, "Audit log write failed for action {Action}", action);
        }
    }
}

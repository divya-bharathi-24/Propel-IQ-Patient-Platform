using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.BackgroundServices;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Exceptions;
using Propel.Modules.Calendar.Options;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="HandleOutlookCallbackCommand"/> for <c>GET /api/calendar/outlook/callback</c>
/// (us_036, AC-2, AC-4, edge case — revoked consent).
/// <list type="number">
///   <item>Decodes and validates the CSRF <c>state</c> parameter.</item>
///   <item>Verifies appointment ownership against the <c>patientId</c> extracted from state (OWASP A01).</item>
///   <item>Exchanges the authorization <c>code</c> for an access token via MSAL.</item>
///   <item>Creates a Microsoft Graph calendar event (<c>POST /me/events</c>) with all FR-036 fields.</item>
///   <item>UPSERTs <c>CalendarSync { provider = Outlook, syncStatus = Synced }</c>.</item>
///   <item>Writes audit log <c>OutlookCalendarSynced</c>.</item>
///   <item>On Graph HTTP 401: sets <c>syncStatus = Revoked</c>; throws <see cref="OutlookCalendarAuthRevokedException"/>.</item>
///   <item>On other Graph failure: sets <c>syncStatus = Failed</c>; enqueues <see cref="OutlookRetryRequest"/> to retry channel.</item>
/// </list>
/// </summary>
public sealed class HandleOutlookCallbackCommandHandler
    : IRequestHandler<HandleOutlookCallbackCommand, CalendarSyncResultDto>
{
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly ICalendarSyncRepository _calendarSyncRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Channel<OutlookRetryRequest> _retryChannel;
    private readonly OutlookCalendarOptions _options;
    private readonly ILogger<HandleOutlookCallbackCommandHandler> _logger;

    public HandleOutlookCallbackCommandHandler(
        IAppointmentBookingRepository appointmentRepo,
        ICalendarSyncRepository calendarSyncRepo,
        IAuditLogRepository auditLogRepo,
        IHttpContextAccessor httpContextAccessor,
        Channel<OutlookRetryRequest> retryChannel,
        IOptions<OutlookCalendarOptions> options,
        ILogger<HandleOutlookCallbackCommandHandler> logger)
    {
        _appointmentRepo     = appointmentRepo;
        _calendarSyncRepo    = calendarSyncRepo;
        _auditLogRepo        = auditLogRepo;
        _httpContextAccessor = httpContextAccessor;
        _retryChannel        = retryChannel;
        _options             = options.Value;
        _logger              = logger;
    }

    public async Task<CalendarSyncResultDto> Handle(
        HandleOutlookCallbackCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: decode and validate CSRF state (OWASP A01) ───────────────
        var (appointmentId, patientId) = DecodeState(request.State);

        // ── Step 2: verify appointment ownership ──────────────────────────────
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(
            appointmentId, cancellationToken);

        if (appointment is null || appointment.PatientId != patientId)
            throw new UnauthorizedAccessException(
                $"Appointment {appointmentId} does not belong to patient {patientId}.");

        // ── Step 3: exchange authorization code for access token (MSAL) ──────
        var app = BuildConfidentialClient();
        var tokenResult = await app
            .AcquireTokenByAuthorizationCode(_options.Scopes, request.Code)
            .ExecuteAsync(cancellationToken);

        var accessToken = tokenResult.AccessToken;

        // ── Step 4: build GraphServiceClient with acquired token ───────────────
        var graphClient = BuildGraphClient(accessToken);

        // ── Step 5: create Graph calendar event (POST /me/events) ────────────
        var startDt = appointment.Date.ToDateTime(appointment.TimeSlotStart ?? TimeOnly.MinValue);
        var endDt   = appointment.Date.ToDateTime(appointment.TimeSlotEnd
                          ?? (appointment.TimeSlotStart?.AddHours(1) ?? TimeOnly.MinValue.AddHours(1)));

        var graphEvent = new Event
        {
            Subject = $"Appointment — {appointment.Specialty?.Name ?? "Unknown"}",
            Body    = new ItemBody
            {
                Content     = $"Booking Reference: {appointment.Id}\nClinic: PropelIQ Clinic",
                ContentType = BodyType.Text
            },
            Start = new DateTimeTimeZone
            {
                DateTime = startDt.ToString("o"),
                TimeZone = "UTC"
            },
            End = new DateTimeTimeZone
            {
                DateTime = endDt.ToString("o"),
                TimeZone = "UTC"
            },
            Location = new Location { DisplayName = "PropelIQ Clinic" }
        };

        Event createdEvent;
        try
        {
            createdEvent = await graphClient.Me.Events.PostAsync(
                graphEvent, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "Microsoft Graph returned null response for event creation.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Unauthorized)
        {
            // Revoked consent — update CalendarSync to Revoked (edge case spec)
            _logger.LogWarning(
                "Outlook OAuth consent revoked for PatientId={PatientId} AppointmentId={AppointmentId}",
                patientId, appointmentId);

            await UpsertCalendarSyncAsync(
                appointmentId, patientId,
                externalEventId: null, eventLink: null,
                status: CalendarSyncStatus.Revoked,
                cancellationToken);

            throw new OutlookCalendarAuthRevokedException(
                "Outlook OAuth consent has been revoked. Patient must reconnect.");
        }
        catch (Exception ex)
        {
            // Other Graph failure — mark Failed + enqueue 10-minute retry (AC-4)
            _logger.LogError(ex,
                "Outlook Graph API event creation failed for PatientId={PatientId} AppointmentId={AppointmentId}",
                patientId, appointmentId);

            await UpsertCalendarSyncAsync(
                appointmentId, patientId,
                externalEventId: null, eventLink: null,
                status: CalendarSyncStatus.Failed,
                cancellationToken);

            await _retryChannel.Writer.WriteAsync(
                new OutlookRetryRequest(appointmentId, patientId, accessToken, DateTime.UtcNow),
                cancellationToken);

            throw;
        }

        // ── Step 6: UPSERT CalendarSync (Synced) ─────────────────────────────
        await UpsertCalendarSyncAsync(
            appointmentId, patientId,
            externalEventId: createdEvent.Id,
            eventLink: createdEvent.WebLink,
            status: CalendarSyncStatus.Synced,
            cancellationToken);

        // ── Step 7: write audit log OutlookCalendarSynced (FR-057) ───────────
        await WriteAuditLogAsync(patientId, appointmentId, "OutlookCalendarSynced", cancellationToken);

        _logger.LogInformation(
            "Outlook Calendar event created for PatientId={PatientId} AppointmentId={AppointmentId} EventId={EventId}",
            patientId, appointmentId, createdEvent.Id);

        return new CalendarSyncResultDto(CalendarSyncStatus.Synced, createdEvent.WebLink);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the Base64-encoded CSRF state parameter into (appointmentId, patientId).
    /// Throws <see cref="InvalidOperationException"/> if the state is malformed (OWASP A01).
    /// </summary>
    private static (Guid AppointmentId, Guid PatientId) DecodeState(string state)
    {
        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("OAuth state parameter is not valid Base64.");
        }

        var parts = decoded.Split(':', count: 2);
        if (parts.Length != 2
            || !Guid.TryParse(parts[0], out var appointmentId)
            || !Guid.TryParse(parts[1], out var patientId))
        {
            throw new InvalidOperationException(
                "OAuth state parameter has an unexpected format. Expected 'appointmentId:patientId'.");
        }

        return (appointmentId, patientId);
    }

    private IConfidentialClientApplication BuildConfidentialClient()
    {
        return ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
            .WithRedirectUri(_options.RedirectUri)
            .Build();
    }

    private static GraphServiceClient BuildGraphClient(string accessToken)
    {
        var tokenProvider = new StaticAccessTokenProvider(accessToken);
        var authProvider  = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }

    private async Task UpsertCalendarSyncAsync(
        Guid appointmentId,
        Guid patientId,
        string? externalEventId,
        string? eventLink,
        CalendarSyncStatus status,
        CancellationToken cancellationToken)
    {
        var calendarSync = new CalendarSync
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            AppointmentId   = appointmentId,
            Provider        = CalendarProvider.Outlook,
            ExternalEventId = externalEventId ?? string.Empty,
            EventLink       = eventLink,
            SyncStatus      = status,
            SyncedAt        = status == CalendarSyncStatus.Synced ? DateTime.UtcNow : null,
            RetryScheduledAt = status == CalendarSyncStatus.Failed
                ? DateTime.UtcNow.AddSeconds(600)  // 10-minute retry (AC-4)
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

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
            // Audit failure must never interrupt the main flow (AG-6)
            _logger.LogWarning(ex, "Audit log write failed for action {Action}", action);
        }
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

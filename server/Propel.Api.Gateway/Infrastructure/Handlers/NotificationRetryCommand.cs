using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// MediatR command carrying a single retry-eligible SMS <see cref="Notification"/> (US_025, AC-3).
/// Dispatched by <see cref="SmsRetryBackgroundService"/> for each eligible record returned by
/// <see cref="SmsRetryRepository.GetRetryEligibleSmsAsync"/>.
/// </summary>
public sealed record NotificationRetryCommand(Notification Notification)
    : IRequest<NotificationRetryResult>;

/// <summary>
/// Result of a single SMS retry attempt (success flag and descriptive outcome token).
/// </summary>
public sealed record NotificationRetryResult(bool Success, string Outcome);

/// <summary>
/// Handles <see cref="NotificationRetryCommand"/> (US_025, AC-3, FR-034):
/// <list type="number">
///   <item><b>Step 1</b> — Fetch <see cref="Appointment"/> date/time and <see cref="Specialty"/> name
///         from the linked <c>AppointmentId</c> to reconstruct the SMS body.</item>
///   <item><b>Step 2</b> — Fetch the patient's E.164 phone number (PHI-encrypted column via
///         EF value converter — decrypted on read, never written to logs).</item>
///   <item><b>Step 3</b> — Call <see cref="ISmsService.SendSlotSwapSmsAsync"/> (graceful
///         degradation: never throws, NFR-018).</item>
///   <item><b>Step 4</b> — UPDATE <see cref="Notification"/>: <c>retryCount = 1</c>,
///         <c>lastRetryAt = UtcNow</c>, <c>status = Sent | Failed</c>.</item>
///   <item><b>Step 5</b> — INSERT <see cref="AuditLog"/> with <c>action = "NotificationRetried"</c>
///         and <c>details = { outcome, channel, retryCount }</c> (FR-034).
///         No PHI in log details or Serilog values (NFR-013, HIPAA).</item>
///   <item><b>Step 6</b> — On retry failure, check whether the sibling Email
///         <see cref="Notification"/> for the same (patientId, appointmentId, templateType)
///         is also <c>Failed</c>. If both are failed, idempotent-upsert a
///         <c>SwapNotificationFailure</c> entry into <c>Patient.PendingAlertsJson</c>
///         (US_025 dual-failure edge case).</item>
/// </list>
/// </summary>
public sealed class NotificationRetryCommandHandler
    : IRequestHandler<NotificationRetryCommand, NotificationRetryResult>
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ISmsService _smsService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<NotificationRetryCommandHandler> _logger;

    public NotificationRetryCommandHandler(
        IDbContextFactory<AppDbContext> contextFactory,
        ISmsService smsService,
        IAuditLogRepository auditLogRepository,
        ILogger<NotificationRetryCommandHandler> logger)
    {
        _contextFactory = contextFactory;
        _smsService = smsService;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task<NotificationRetryResult> Handle(
        NotificationRetryCommand request,
        CancellationToken cancellationToken)
    {
        var notif = request.Notification;

        // Guard: appointment linkage is required to reconstruct the SMS payload.
        if (notif.AppointmentId is null)
        {
            _logger.LogWarning(
                "SmsRetry_NoAppointmentId: NotificationId={NotificationId} — skipping retry.",
                notif.Id);
            return new NotificationRetryResult(false, "NoAppointmentId");
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 1 — Fetch appointment payload needed to build the SMS body.
        var appt = await db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == notif.AppointmentId.Value)
            .Select(a => new { a.Date, a.TimeSlotStart, a.SpecialtyId })
            .FirstOrDefaultAsync(cancellationToken);

        if (appt is null)
        {
            _logger.LogWarning(
                "SmsRetry_AppointmentNotFound: NotificationId={NotificationId} AppointmentId={AppointmentId} — skipping retry.",
                notif.Id, notif.AppointmentId);
            return new NotificationRetryResult(false, "AppointmentNotFound");
        }

        string specialtyName = await db.Specialties
            .AsNoTracking()
            .Where(s => s.Id == appt.SpecialtyId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Healthcare";

        // Step 2 — Fetch patient phone number (PHI — never logged).
        string? patientPhone = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == notif.PatientId)
            .Select(p => p.Phone)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(patientPhone))
        {
            _logger.LogWarning(
                "SmsRetry_NoPhone: NotificationId={NotificationId} — skipping retry.",
                notif.Id);
            return new NotificationRetryResult(false, "NoPhone");
        }

        // Booking reference: same format used by SwapNotificationHandler (task_001).
        string bookingReference =
            $"APT-{notif.AppointmentId.Value.ToString("N")[..8].ToUpperInvariant()}";

        // Slot-swap SMS retry is only applicable for appointments with a time slot.
        // Walk-in queued-only appointments (TimeSlotStart = null) will never reach this
        // code path since they are not associated with slot-swap notifications.
        if (!appt.TimeSlotStart.HasValue)
        {
            _logger.LogWarning(
                "SmsRetry_NoTimeSlot: NotificationId={NotificationId} AppointmentId={AppointmentId} — skipping retry for queued-only appointment.",
                notif.Id, notif.AppointmentId);
            return new NotificationRetryResult(false, "NoTimeSlot");
        }

        // Step 3 — Call Twilio (never throws; graceful degradation per NFR-018).
        var smsResult = await _smsService.SendSlotSwapSmsAsync(
            toPhoneNumber: patientPhone,
            appointmentDate: appt.Date,
            appointmentTimeStart: appt.TimeSlotStart.Value,
            specialtyName: specialtyName,
            bookingReference: bookingReference,
            cancellationToken: cancellationToken);

        string outcome = smsResult.Success ? "RetrySucceeded" : "RetryFailed";

        // Step 4 — UPDATE Notification: retryCount = 1, lastRetryAt = UtcNow.
        var trackedNotif = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notif.Id, cancellationToken);

        if (trackedNotif is not null)
        {
            trackedNotif.RetryCount  = 1;
            trackedNotif.LastRetryAt = DateTime.UtcNow;
            trackedNotif.UpdatedAt   = DateTime.UtcNow;
            trackedNotif.Status      = smsResult.Success
                ? NotificationStatus.Sent
                : NotificationStatus.Failed;

            if (smsResult.Success)
                trackedNotif.SentAt = smsResult.DeliveryTimestamp ?? DateTime.UtcNow;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SmsRetry_UpdateFailed: NotificationId={NotificationId}",
                    notif.Id);
            }
        }

        // Step 5 — INSERT AuditLog — no PHI in details (FR-034, NFR-013, HIPAA).
        await AppendAuditLogAsync(notif, outcome, cancellationToken);

        _logger.LogInformation(
            "SmsRetry_{Outcome}: NotificationId={NotificationId} AppointmentId={AppointmentId}",
            outcome, notif.Id, notif.AppointmentId);

        // Step 6 — On retry failure, check for dual-failure and flag dashboard alert.
        if (!smsResult.Success)
            await CheckDualFailureAsync(db, notif, cancellationToken);

        return new NotificationRetryResult(smsResult.Success, outcome);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Inserts an immutable <see cref="AuditLog"/> entry per retry attempt (FR-034).
    /// Details contain only the outcome token, channel, and retryCount — no PHI (NFR-013).
    /// </summary>
    private async Task AppendAuditLogAsync(
        Notification notif,
        string outcome,
        CancellationToken ct)
    {
        try
        {
            await _auditLogRepository.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                PatientId  = notif.PatientId,
                Role       = "System",
                Action     = "NotificationRetried",
                EntityType = nameof(Notification),
                EntityId   = notif.Id,
                Details    = JsonDocument.Parse(
                    $$$"""{"outcome":"{{{outcome}}}","channel":"SMS","retryCount":1}"""),
                Timestamp  = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SmsRetry_AuditLogFailed: NotificationId={NotificationId}",
                notif.Id);
        }
    }

    /// <summary>
    /// Checks whether the sibling Email <see cref="Notification"/> for the same
    /// (patientId, appointmentId, templateType) is also <c>Failed</c>.
    /// If both channels failed, idempotent-upserts a <c>SwapNotificationFailure</c> entry
    /// into <c>Patient.PendingAlertsJson</c> so the dashboard can surface the alert on next
    /// patient login (US_025 dual-failure edge case).
    /// Idempotency: skips the write if an alert for the same appointmentId already exists,
    /// so repeated background-service runs never produce duplicate alerts.
    /// </summary>
    private async Task CheckDualFailureAsync(
        AppDbContext db,
        Notification failedSms,
        CancellationToken ct)
    {
        try
        {
            bool emailAlsoFailed = await db.Notifications
                .AsNoTracking()
                .AnyAsync(n =>
                    n.PatientId    == failedSms.PatientId    &&
                    n.AppointmentId == failedSms.AppointmentId &&
                    n.TemplateType == failedSms.TemplateType  &&
                    n.Channel      == NotificationChannel.Email &&
                    n.Status       == NotificationStatus.Failed,
                    ct);

            if (!emailAlsoFailed) return;

            // Isolated tracking context for the patient update (AD-7 pattern).
            await using var trackingDb = await _contextFactory.CreateDbContextAsync(ct);
            var patient = await trackingDb.Patients
                .FirstOrDefaultAsync(p => p.Id == failedSms.PatientId, ct);

            if (patient is null) return;

            // Deserialize existing alerts to check idempotency before writing.
            List<object> alerts = [];
            if (!string.IsNullOrWhiteSpace(patient.PendingAlertsJson))
            {
                try
                {
                    alerts = JsonSerializer.Deserialize<List<object>>(
                        patient.PendingAlertsJson) ?? [];
                }
                catch (JsonException)
                {
                    alerts = [];
                }
            }

            // Idempotency guard: skip if an alert for this appointment already exists.
            bool alreadyFlagged = alerts.Any(el =>
                el is JsonElement jsonEl &&
                jsonEl.TryGetProperty("alertType", out var alertType) &&
                alertType.GetString() == "SwapNotificationFailure" &&
                jsonEl.TryGetProperty("appointmentId", out var apptIdEl) &&
                apptIdEl.ValueKind != JsonValueKind.Null &&
                (apptIdEl.TryGetGuid(out var apptGuid)
                    ? apptGuid == failedSms.AppointmentId
                    : apptIdEl.GetString() == failedSms.AppointmentId?.ToString()));

            if (alreadyFlagged) return;

            string bookingReference = failedSms.AppointmentId.HasValue
                ? $"APT-{failedSms.AppointmentId.Value.ToString("N")[..8].ToUpperInvariant()}"
                : string.Empty;

            alerts.Add(new
            {
                alertType        = "SwapNotificationFailure",
                appointmentId    = failedSms.AppointmentId,
                bookingReference,
                createdAt        = DateTime.UtcNow
            });

            patient.PendingAlertsJson = JsonSerializer.Serialize(alerts);
            await trackingDb.SaveChangesAsync(ct);

            _logger.LogWarning(
                "SmsRetry_DualFailure: PatientId={PatientId} AppointmentId={AppointmentId} " +
                "— PendingAlert SwapNotificationFailure written.",
                failedSms.PatientId, failedSms.AppointmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SmsRetry_DualFailureFlagFailed: PatientId={PatientId}",
                failedSms.PatientId);
        }
    }
}

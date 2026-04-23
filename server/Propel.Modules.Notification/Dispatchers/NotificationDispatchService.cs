using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Notifiers;
using AuditLogEntity = Propel.Domain.Entities.AuditLog;
using NotificationEntity = Propel.Domain.Entities.Notification;

namespace Propel.Modules.Notification.Dispatchers;

/// <summary>
/// Orchestrates email and SMS dispatch for a single <see cref="Notification"/> record (US_033, AC-2).
/// <para>
/// <b>Email channel:</b> Sends via <see cref="IEmailNotifier"/>. On failure, marks the record
/// as <c>Failed</c> immediately — email is not retried (NFR-018 graceful degradation). A failed
/// email channel record does not block SMS dispatch for the companion SMS record.
/// </para>
/// <para>
/// <b>SMS channel:</b> Sends via <see cref="ISmsNotifier"/>. On first failure
/// (<c>retryCount &lt; 1</c>), increments <c>retryCount</c>, sets <c>lastRetryAt = UtcNow + 5min</c>,
/// and keeps <c>status = Pending</c> so the scheduler's next tick re-queues the record
/// (US_033 Edge Case 1). On second failure, marks the record as <c>Failed</c>.
/// </para>
/// <para>
/// <b>Audit:</b> After every dispatch attempt, an immutable <see cref="AuditLog"/> entry is
/// appended with action <c>NotificationSent</c> or <c>NotificationFailed</c> (NFR-009, TR-018).
/// Audit write failures are swallowed to ensure they never interrupt the dispatch pipeline (OWASP A09).
/// </para>
/// No PHI is written to Serilog structured log values (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class NotificationDispatchService : INotificationDispatcher
{
    private readonly IEmailNotifier _emailNotifier;
    private readonly ISmsNotifier _smsNotifier;
    private readonly INotificationRepository _notificationRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPatientRepository _patientRepo;
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(
        IEmailNotifier emailNotifier,
        ISmsNotifier smsNotifier,
        INotificationRepository notificationRepo,
        IAuditLogRepository auditLogRepo,
        IPatientRepository patientRepo,
        IAppointmentBookingRepository appointmentRepo,
        ILogger<NotificationDispatchService> logger)
    {
        _emailNotifier    = emailNotifier;
        _smsNotifier      = smsNotifier;
        _notificationRepo = notificationRepo;
        _auditLogRepo     = auditLogRepo;
        _patientRepo      = patientRepo;
        _appointmentRepo  = appointmentRepo;
        _logger           = logger;
    }

    /// <inheritdoc/>
    public async Task DispatchAsync(NotificationEntity notification, CancellationToken cancellationToken = default)
    {
        if (notification.AppointmentId is null)
        {
            _logger.LogWarning(
                "NotificationDispatch_Skipped: notification {NotifId} has no AppointmentId.",
                notification.Id);
            return;
        }

        // ── Resolve patient contact details (no PHI in log values — NFR-013) ─
        var patient = await _patientRepo.GetCommunicationPreferencesAsync(
            notification.PatientId, cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "NotificationDispatch_Skipped: patient not found for notification {NotifId}.",
                notification.Id);
            await FailNotificationAsync(notification, "Patient record not found.", cancellationToken);
            return;
        }

        // ── Resolve appointment data ──────────────────────────────────────────
        var appointment = await _appointmentRepo.GetByIdWithRelatedAsync(
            notification.AppointmentId.Value, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "NotificationDispatch_Skipped: appointment not found for notification {NotifId}.",
                notification.Id);
            await FailNotificationAsync(notification, "Appointment record not found.", cancellationToken);
            return;
        }

        if (appointment.TimeSlotStart is null)
        {
            _logger.LogWarning(
                "NotificationDispatch_Skipped: appointment has no time slot for notification {NotifId}.",
                notification.Id);
            await FailNotificationAsync(notification, "Appointment has no scheduled time slot.", cancellationToken);
            return;
        }

        string? specialtyName = await _appointmentRepo.GetSpecialtyNameAsync(
            appointment.SpecialtyId, cancellationToken);

        // Reference number is deterministically derived from appointment ID (US_019 pattern).
        string referenceNumber =
            $"APT-{notification.AppointmentId.Value.ToString("N")[..8].ToUpperInvariant()}";

        var payload = new ReminderPayload(
            PatientName:       patient.Value.Name,
            AppointmentDate:   appointment.Date,
            AppointmentTimeSlot: appointment.TimeSlotStart.Value,
            ProviderSpecialty: specialtyName ?? "General",
            ReferenceNumber:   referenceNumber);

        var utcNow = DateTime.UtcNow;

        // ── Email channel ─────────────────────────────────────────────────────
        if (notification.Channel == NotificationChannel.Email)
        {
            var result = await _emailNotifier.SendAsync(patient.Value.Email, payload, cancellationToken);

            if (result.IsSuccess)
            {
                notification.Status       = NotificationStatus.Sent;
                notification.SentAt       = utcNow;
                notification.ErrorMessage = null;
            }
            else
            {
                // Email failure is non-retryable; SMS companion record is unaffected (NFR-018).
                notification.Status       = NotificationStatus.Failed;
                notification.ErrorMessage = result.ErrorMessage;
                _logger.LogWarning(
                    "NotificationDispatch_EmailFailed: notification {NotifId} ref={Ref}. Error: {Error}",
                    notification.Id, referenceNumber, result.ErrorMessage);
            }
        }

        // ── SMS channel ───────────────────────────────────────────────────────
        else if (notification.Channel == NotificationChannel.Sms)
        {
            var result = await _smsNotifier.SendAsync(patient.Value.Phone, payload, cancellationToken);

            if (result.IsSuccess)
            {
                notification.Status       = NotificationStatus.Sent;
                notification.SentAt       = utcNow;
                notification.ErrorMessage = null;
            }
            else if (notification.RetryCount < 1)
            {
                // First failure — schedule retry; scheduler picks up when lastRetryAt <= UtcNow.
                notification.RetryCount++;
                notification.LastRetryAt  = utcNow.AddMinutes(5);
                notification.Status       = NotificationStatus.Pending;
                notification.ErrorMessage = result.ErrorMessage;
                _logger.LogWarning(
                    "NotificationDispatch_SmsRetryScheduled: notification {NotifId} ref={Ref}, " +
                    "retryCount={RetryCount}. Error: {Error}",
                    notification.Id, referenceNumber, notification.RetryCount, result.ErrorMessage);
            }
            else
            {
                // Second failure — mark permanently failed; no further retries.
                notification.Status       = NotificationStatus.Failed;
                notification.ErrorMessage = result.ErrorMessage;
                _logger.LogWarning(
                    "NotificationDispatch_SmsFailed: notification {NotifId} ref={Ref} after " +
                    "{RetryCount} retries. Error: {Error}",
                    notification.Id, referenceNumber, notification.RetryCount, result.ErrorMessage);
            }
        }
        else
        {
            _logger.LogWarning(
                "NotificationDispatch_UnsupportedChannel: notification {NotifId} channel={Channel}.",
                notification.Id, notification.Channel);
            return;
        }

        notification.UpdatedAt = utcNow;
        await _notificationRepo.UpdateAsync(notification, cancellationToken);

        // Append audit log entry — failure must never interrupt the dispatch pipeline (OWASP A09).
        await AppendAuditAsync(notification, cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task FailNotificationAsync(
        NotificationEntity notification,
        string reason,
        CancellationToken ct)
    {
        notification.Status       = NotificationStatus.Failed;
        notification.ErrorMessage = reason;
        notification.UpdatedAt    = DateTime.UtcNow;
        await _notificationRepo.UpdateAsync(notification, ct);
        await AppendAuditAsync(notification, ct);
    }

    private async Task AppendAuditAsync(NotificationEntity notification, CancellationToken ct)
    {
        try
        {
            string action = notification.Status == NotificationStatus.Sent
                ? "NotificationSent"
                : "NotificationFailed";

            await _auditLogRepo.AppendAsync(new AuditLogEntity
            {
                Id            = Guid.NewGuid(),
                UserId        = null,           // system-initiated; no authenticated user
                PatientId     = notification.PatientId,
                Role          = "System",
                Action        = action,
                EntityType    = "Notification",
                EntityId      = notification.Id,
                IpAddress     = null,
                CorrelationId = null,
                Timestamp     = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            // Audit write failures must never interrupt the dispatch pipeline (OWASP A09, NFR-009).
            _logger.LogWarning(ex,
                "NotificationDispatch_AuditWriteFailed: could not persist audit entry for notification {NotifId}.",
                notification.Id);
        }
    }
}

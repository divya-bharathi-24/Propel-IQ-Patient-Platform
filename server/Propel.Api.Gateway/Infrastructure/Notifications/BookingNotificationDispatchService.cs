using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Dispatchers;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Notifiers;

namespace Propel.Api.Gateway.Infrastructure.Notifications;

/// <summary>
/// Fire-and-try implementation of <see cref="INotificationDispatchService"/> for booking
/// confirmation and reminder notifications dispatched from command handlers (US_052, AC-2).
/// <para>
/// Persists a <see cref="Notification"/> record with <c>Status = Pending</c> <b>before</b>
/// attempting delivery so no notification is silently lost on process crash.
/// On delivery failure the record remains <c>Pending</c> and is picked up by
/// <c>NotificationRetryBackgroundService</c> (exponential backoff: 4^retryCount minutes,
/// max 3 retries). On success the record is updated to <c>Sent</c>.
/// </para>
/// Never throws — all exceptions are caught, logged at Warning level, and translated to a
/// <see cref="NotificationResult"/> so the calling booking command always continues (NFR-018).
/// No PHI is written to Serilog structured log properties (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class BookingNotificationDispatchService : INotificationDispatchService
{
    private readonly IEmailNotifier _emailNotifier;
    private readonly ISmsNotifier _smsNotifier;
    private readonly INotificationRepository _notificationRepo;
    private readonly IPatientRepository _patientRepo;
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly ILogger<BookingNotificationDispatchService> _logger;

    public BookingNotificationDispatchService(
        IEmailNotifier emailNotifier,
        ISmsNotifier smsNotifier,
        INotificationRepository notificationRepo,
        IPatientRepository patientRepo,
        IAppointmentBookingRepository appointmentRepo,
        ILogger<BookingNotificationDispatchService> logger)
    {
        _emailNotifier    = emailNotifier;
        _smsNotifier      = smsNotifier;
        _notificationRepo = notificationRepo;
        _patientRepo      = patientRepo;
        _appointmentRepo  = appointmentRepo;
        _logger           = logger;
    }

    /// <inheritdoc/>
    public async Task<NotificationResult> SendEmailAsync(
        Guid patientId,
        Guid appointmentId,
        string templateType,
        string toEmail,
        CancellationToken ct = default)
    {
        var notification = await PersistPendingAsync(
            patientId, appointmentId, templateType, NotificationChannel.Email, ct);

        if (notification is null)
            return new NotificationResult(NotificationResultStatus.Failed, "Failed to persist notification record.");

        ReminderPayload? payload = await BuildPayloadAsync(notification, ct);
        if (payload is null)
            return await MarkFailedAsync(notification, "Could not resolve appointment data for email payload.", ct);

        try
        {
            NotifierResult result = await _emailNotifier.SendAsync(toEmail, payload, ct);

            if (result.IsSuccess)
                return await MarkSentAsync(notification, ct);

            _logger.LogWarning(
                "BookingEmailDispatch_Failed: notification {NotifId} queued for retry. Error: {Error}",
                notification.Id, result.ErrorMessage);

            return new NotificationResult(NotificationResultStatus.Queued, "Notification pending delivery.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BookingEmailDispatch_Exception: notification {NotifId} queued for retry.",
                notification.Id);

            return new NotificationResult(NotificationResultStatus.Queued, "Notification pending delivery.");
        }
    }

    /// <inheritdoc/>
    public async Task<NotificationResult> SendSmsAsync(
        Guid patientId,
        Guid appointmentId,
        string templateType,
        string toPhone,
        CancellationToken ct = default)
    {
        var notification = await PersistPendingAsync(
            patientId, appointmentId, templateType, NotificationChannel.Sms, ct);

        if (notification is null)
            return new NotificationResult(NotificationResultStatus.Failed, "Failed to persist notification record.");

        ReminderPayload? payload = await BuildPayloadAsync(notification, ct);
        if (payload is null)
            return await MarkFailedAsync(notification, "Could not resolve appointment data for SMS payload.", ct);

        try
        {
            NotifierResult result = await _smsNotifier.SendAsync(toPhone, payload, ct);

            if (result.IsSuccess)
                return await MarkSentAsync(notification, ct);

            _logger.LogWarning(
                "BookingSmsDispatch_Failed: notification {NotifId} queued for retry. Error: {Error}",
                notification.Id, result.ErrorMessage);

            return new NotificationResult(NotificationResultStatus.Queued, "Notification pending delivery.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BookingSmsDispatch_Exception: notification {NotifId} queued for retry.",
                notification.Id);

            return new NotificationResult(NotificationResultStatus.Queued, "Notification pending delivery.");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Persists a new <see cref="Notification"/> record with <c>Status = Pending</c> before
    /// attempting delivery — ensures no notification is lost if the process crashes (US_052, AC-2).
    /// Returns <c>null</c> and logs a warning if persistence fails.
    /// </summary>
    private async Task<Notification?> PersistPendingAsync(
        Guid patientId,
        Guid appointmentId,
        string templateType,
        NotificationChannel channel,
        CancellationToken ct)
    {
        try
        {
            var utcNow = DateTime.UtcNow;
            var notification = new Notification
            {
                Id            = Guid.NewGuid(),
                PatientId     = patientId,
                AppointmentId = appointmentId,
                Channel       = channel,
                TemplateType  = templateType,
                Status        = NotificationStatus.Pending,
                RetryCount    = 0,
                LastRetryAt   = utcNow,
                CreatedAt     = utcNow,
                UpdatedAt     = utcNow
            };

            await _notificationRepo.InsertAsync(notification, ct);
            return notification;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BookingNotificationPersist_Failed: could not create Pending record for patient {PatientId}.",
                patientId);
            return null;
        }
    }

    /// <summary>
    /// Resolves appointment and specialty data to construct the <see cref="ReminderPayload"/>
    /// required by the email and SMS notifiers.
    /// Returns <c>null</c> if the appointment cannot be found or has no scheduled time slot.
    /// </summary>
    private async Task<ReminderPayload?> BuildPayloadAsync(Notification notification, CancellationToken ct)
    {
        if (notification.AppointmentId is null)
            return null;

        var appointment = await _appointmentRepo.GetByIdWithRelatedAsync(
            notification.AppointmentId.Value, ct);

        if (appointment is null || appointment.TimeSlotStart is null)
            return null;

        var patient = await _patientRepo.GetCommunicationPreferencesAsync(
            notification.PatientId, ct);

        if (patient is null)
            return null;

        string? specialtyName = await _appointmentRepo.GetSpecialtyNameAsync(
            appointment.SpecialtyId, ct);

        string referenceNumber =
            $"APT-{notification.AppointmentId.Value.ToString("N")[..8].ToUpperInvariant()}";

        return new ReminderPayload(
            PatientName:         patient.Value.Name,
            AppointmentDate:     appointment.Date,
            AppointmentTimeSlot: appointment.TimeSlotStart.Value,
            ProviderSpecialty:   specialtyName ?? "General",
            ReferenceNumber:     referenceNumber);
    }

    private async Task<NotificationResult> MarkSentAsync(Notification notification, CancellationToken ct)
    {
        notification.Status    = NotificationStatus.Sent;
        notification.SentAt    = DateTime.UtcNow;
        notification.UpdatedAt = DateTime.UtcNow;
        await _notificationRepo.UpdateAsync(notification, ct);
        return new NotificationResult(NotificationResultStatus.Sent);
    }

    private async Task<NotificationResult> MarkFailedAsync(
        Notification notification, string reason, CancellationToken ct)
    {
        notification.Status       = NotificationStatus.Failed;
        notification.ErrorMessage = reason;
        notification.UpdatedAt    = DateTime.UtcNow;
        await _notificationRepo.UpdateAsync(notification, ct);
        _logger.LogWarning(
            "BookingNotificationDispatch_Failed: notification {NotifId} marked Failed. Reason: {Reason}",
            notification.Id, reason);
        return new NotificationResult(NotificationResultStatus.Failed, reason);
    }
}

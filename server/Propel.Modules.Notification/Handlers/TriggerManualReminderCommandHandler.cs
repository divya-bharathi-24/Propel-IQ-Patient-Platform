using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Commands;
using Propel.Modules.Notification.Exceptions;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Notifiers;
using NotificationEntity = Propel.Domain.Entities.Notification;

namespace Propel.Modules.Notification.Handlers;

/// <summary>
/// Handles <see cref="TriggerManualReminderCommand"/> for
/// <c>POST /api/staff/appointments/{appointmentId}/reminders/trigger</c> (US_034, AC-1–AC-4).
/// <list type="number">
///   <item><b>Step 1 — Load appointment</b>: 404 when not found.</item>
///   <item><b>Step 2 — Status check</b>: throws <see cref="CancelledAppointmentReminderException"/>
///         (→ HTTP 422) when <c>status == Cancelled</c> (AC-1 edge case).</item>
///   <item><b>Step 3 — Anonymous walk-in guard</b>: 404 when <c>PatientId</c> is null
///         (anonymous appointments have no contact details for delivery).</item>
///   <item><b>Step 4 — Debounce check</b>: throws <see cref="ReminderCooldownException"/>
///         (→ HTTP 429) when a <c>Sent</c> manual reminder exists within the last 5 minutes
///         (AC-2 edge case).</item>
///   <item><b>Step 5 — Resolve patient contact details</b> via <see cref="IPatientRepository"/>.</item>
///   <item><b>Step 6 — Build reminder payload</b> (patient name, date, time, specialty, reference).</item>
///   <item><b>Step 7 — Dispatch email</b> via <see cref="IEmailNotifier"/> (SendGrid).</item>
///   <item><b>Step 8 — Dispatch SMS</b> via <see cref="ISmsNotifier"/> (Twilio).</item>
///   <item><b>Step 9 — Persist Notification records</b>: one per channel with
///         <c>TriggeredBy = StaffUserId</c>, <c>Status = Sent|Failed</c>,
///         <c>SentAt = UtcNow</c>, <c>ErrorReason</c> from provider error (AC-2, AC-4).</item>
/// </list>
/// PHI (patient name, email, phone) is never written to Serilog structured log properties
/// (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class TriggerManualReminderCommandHandler
    : IRequestHandler<TriggerManualReminderCommand, TriggerManualReminderResponseDto>
{
    private const int DebounceMintes = 5;
    private const string AdHocTemplateType = "AdHocReminder";

    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly INotificationRepository _notificationRepo;
    private readonly IPatientRepository _patientRepo;
    private readonly IUserRepository _userRepo;
    private readonly IEmailNotifier _emailNotifier;
    private readonly ISmsNotifier _smsNotifier;
    private readonly ILogger<TriggerManualReminderCommandHandler> _logger;

    public TriggerManualReminderCommandHandler(
        IAppointmentBookingRepository appointmentRepo,
        INotificationRepository notificationRepo,
        IPatientRepository patientRepo,
        IUserRepository userRepo,
        IEmailNotifier emailNotifier,
        ISmsNotifier smsNotifier,
        ILogger<TriggerManualReminderCommandHandler> logger)
    {
        _appointmentRepo  = appointmentRepo;
        _notificationRepo = notificationRepo;
        _patientRepo      = patientRepo;
        _userRepo         = userRepo;
        _emailNotifier    = emailNotifier;
        _smsNotifier      = smsNotifier;
        _logger           = logger;
    }

    public async Task<TriggerManualReminderResponseDto> Handle(
        TriggerManualReminderCommand command,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load appointment.
        var appointment = await _appointmentRepo.GetByIdWithRelatedAsync(
            command.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "TriggerManualReminder_NotFound: AppointmentId={AppointmentId}",
                command.AppointmentId);
            throw new KeyNotFoundException($"Appointment '{command.AppointmentId}' was not found.");
        }

        // Step 2 — Status check: cannot send to cancelled appointments (AC-1 edge case → HTTP 422).
        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            _logger.LogWarning(
                "TriggerManualReminder_Cancelled: AppointmentId={AppointmentId}",
                command.AppointmentId);
            throw new CancelledAppointmentReminderException();
        }

        // Step 3 — Anonymous walk-in guard: no patient contact info available.
        if (appointment.PatientId is null)
        {
            _logger.LogWarning(
                "TriggerManualReminder_Anonymous: AppointmentId={AppointmentId}",
                command.AppointmentId);
            throw new KeyNotFoundException(
                "Cannot send reminder for an anonymous walk-in appointment — no patient contact details.");
        }

        // Step 4 — Debounce: reject if a Sent manual reminder exists within the last 5 minutes (AC-2 edge case → HTTP 429).
        var recentSent = await _notificationRepo.GetLatestSentManualReminderAsync(
            command.AppointmentId, DebounceMintes, cancellationToken);

        if (recentSent is not null)
        {
            var retryAfterSeconds = (int)Math.Ceiling(
                (recentSent.SentAt!.Value.AddMinutes(DebounceMintes) - DateTime.UtcNow).TotalSeconds);
            throw new ReminderCooldownException(Math.Max(1, retryAfterSeconds));
        }

        // Step 5 — Resolve patient contact details (no PHI in log values — NFR-013).
        var patient = await _patientRepo.GetCommunicationPreferencesAsync(
            appointment.PatientId.Value, cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "TriggerManualReminder_PatientNotFound: AppointmentId={AppointmentId}",
                command.AppointmentId);
            throw new KeyNotFoundException("Patient record not found for this appointment.");
        }

        // Step 6 — Build reminder payload.
        var specialtyName = await _appointmentRepo.GetSpecialtyNameAsync(
            appointment.SpecialtyId, cancellationToken);

        // Reference number is deterministically derived from appointment ID (US_019 pattern).
        var referenceNumber =
            $"APT-{command.AppointmentId.ToString("N")[..8].ToUpperInvariant()}";

        var payload = new ReminderPayload(
            PatientName:         patient.Value.Name,
            AppointmentDate:     appointment.Date,
            AppointmentTimeSlot: appointment.TimeSlotStart ?? TimeOnly.MinValue,
            ProviderSpecialty:   specialtyName ?? "General",
            ReferenceNumber:     referenceNumber);

        var utcNow = DateTime.UtcNow;

        // Step 7 — Dispatch email (SendGrid). Non-throwing by contract — result carries error (NFR-018).
        var emailResult = await _emailNotifier.SendAsync(patient.Value.Email, payload, cancellationToken);

        // Step 8 — Dispatch SMS (Twilio). Non-throwing by contract — result carries error (NFR-018).
        var smsResult = await _smsNotifier.SendAsync(patient.Value.Phone, payload, cancellationToken);

        // Step 9 — Persist one Notification record per channel (AC-2, AC-4).
        var emailNotification = new NotificationEntity
        {
            Id            = Guid.NewGuid(),
            PatientId     = appointment.PatientId.Value,
            AppointmentId = command.AppointmentId,
            Channel       = NotificationChannel.Email,
            TemplateType  = AdHocTemplateType,
            Status        = emailResult.IsSuccess ? NotificationStatus.Sent : NotificationStatus.Failed,
            SentAt        = utcNow,
            TriggeredBy   = command.StaffUserId,
            ErrorReason   = emailResult.IsSuccess ? null : emailResult.ErrorMessage,
            CreatedAt     = utcNow,
            UpdatedAt     = utcNow
        };

        var smsNotification = new NotificationEntity
        {
            Id            = Guid.NewGuid(),
            PatientId     = appointment.PatientId.Value,
            AppointmentId = command.AppointmentId,
            Channel       = NotificationChannel.Sms,
            TemplateType  = AdHocTemplateType,
            Status        = smsResult.IsSuccess ? NotificationStatus.Sent : NotificationStatus.Failed,
            SentAt        = utcNow,
            TriggeredBy   = command.StaffUserId,
            ErrorReason   = smsResult.IsSuccess ? null : smsResult.ErrorMessage,
            CreatedAt     = utcNow,
            UpdatedAt     = utcNow
        };

        await _notificationRepo.InsertAsync(emailNotification, cancellationToken);
        await _notificationRepo.InsertAsync(smsNotification, cancellationToken);

        _logger.LogInformation(
            "TriggerManualReminder: appointment={AppointmentId} triggeredBy={StaffUserId} " +
            "emailStatus={EmailStatus} smsStatus={SmsStatus}",
            command.AppointmentId, command.StaffUserId,
            emailNotification.Status, smsNotification.Status);

        // Resolve staff display name for the response (no PHI exposure — staff name is role-appropriate).
        var staffUser = await _userRepo.GetByIdAsync(command.StaffUserId, cancellationToken);
        var staffName = staffUser?.Name ?? "Staff";

        return new TriggerManualReminderResponseDto(
            SentAt:                utcNow,
            TriggeredByStaffName:  staffName,
            EmailSent:             emailResult.IsSuccess,
            SmsSent:               smsResult.IsSuccess,
            EmailErrorReason:      emailResult.IsSuccess ? null : emailResult.ErrorMessage,
            SmsErrorReason:        smsResult.IsSuccess   ? null : smsResult.ErrorMessage);
    }
}

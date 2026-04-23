using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Events;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// MediatR handler for <see cref="SlotSwapCompletedEvent"/> (US_025, AC-1, AC-2, AC-4).
/// Implements the dual-channel notification pipeline after a slot swap transaction commits:
/// <list type="number">
///   <item><b>Step 1 — Load patient contact data</b>: fetches email, name, phone, and
///         communication preference flags from the <c>patients</c> table via
///         <see cref="IDbContextFactory{TContext}"/> (non-request-scoped AD-7 pattern).</item>
///   <item><b>Step 2 — Communication preference check</b>: reads <c>SmsEnabled</c> from
///         <see cref="PatientCommunicationPreferences"/> and checks that the phone field is
///         non-empty. If either condition fails → <c>skipSms = true</c>; a <c>Notification</c>
///         record with <c>TemplateType = "SlotSwapSmsSkipped"</c> is inserted and logged (AC-4).</item>
///   <item><b>Step 3 — Email dispatch</b>: calls <see cref="IEmailService.SendSlotSwapEmailAsync"/>;
///         inserts a <c>Notification</c> record with <c>channel = Email</c>,
///         <c>status = Sent|Failed</c>, <c>sentAt</c> = UtcNow on success (AC-1, AC-2).</item>
///   <item><b>Step 4 — SMS dispatch</b> (when not skipped): calls
///         <see cref="ISmsService.SendSlotSwapSmsAsync"/>; inserts a <c>Notification</c> record
///         with <c>channel = SMS</c>, <c>status = Sent|Failed</c> (AC-1, AC-2).</item>
///   <item><b>Step 5 — Dual-failure flag</b>: if both email and SMS result in <c>Failed</c>,
///         appends a <c>SwapNotificationFailure</c> entry to <c>Patient.PendingAlertsJson</c>
///         so the dashboard can surface the alert on next login (edge case spec).</item>
///   <item><b>Step 6 — Audit log</b>: appends one <c>AuditLog</c> entry per dispatched
///         <c>Notification</c> record (FR-034, NFR-009, AD-7).</item>
/// </list>
/// All exceptions are swallowed — notification failures must never block the slot swap response
/// or propagate to the MediatR pipeline caller (AG-6, NFR-018).
/// No PHI (email address, phone number) is written to Serilog log values (NFR-013, HIPAA).
/// </summary>
public sealed class SwapNotificationHandler : INotificationHandler<SlotSwapCompletedEvent>
{
    private const string EmailTemplate   = "SlotSwapNotification";
    private const string SmsTemplate     = "SlotSwapNotification";
    private const string SmsSkipTemplate = "SlotSwapSmsSkipped";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly INotificationRepository _notificationRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SwapNotificationHandler> _logger;

    public SwapNotificationHandler(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IEmailService emailService,
        ISmsService smsService,
        INotificationRepository notificationRepository,
        IAuditLogRepository auditLogRepository,
        ILogger<SwapNotificationHandler> logger)
    {
        _dbContextFactory     = dbContextFactory;
        _emailService         = emailService;
        _smsService           = smsService;
        _notificationRepository = notificationRepository;
        _auditLogRepository   = auditLogRepository;
        _logger               = logger;
    }

    public async Task Handle(SlotSwapCompletedEvent notification, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await DispatchAsync(notification, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation — application is shutting down.
        }
        catch (Exception ex)
        {
            // Failures in the notification pipeline must never surface to the caller (NFR-018).
            _logger.LogError(ex,
                "SwapNotification_UnhandledError: PatientId={PatientId} NewAppointmentId={NewAppointmentId}",
                notification.PatientId, notification.NewAppointmentId);
        }

        sw.Stop();
        _logger.LogInformation(
            "SwapNotification_Complete: PatientId={PatientId} NewAppointmentId={NewAppointmentId} DurationMs={DurationMs}",
            notification.PatientId, notification.NewAppointmentId, sw.ElapsedMilliseconds);
    }

    // ── Private orchestration methods ─────────────────────────────────────────

    private async Task DispatchAsync(SlotSwapCompletedEvent evt, CancellationToken ct)
    {
        // Step 1 — Load patient contact data and specialty name.
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var patient = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == evt.PatientId)
            .Select(p => new
            {
                p.Email,
                p.Name,
                p.Phone,
                p.CommunicationPreferencesJson,
                p.PendingAlertsJson
            })
            .FirstOrDefaultAsync(ct);

        if (patient is null)
        {
            _logger.LogWarning(
                "SwapNotification_PatientNotFound: PatientId={PatientId} — notification aborted.",
                evt.PatientId);
            return;
        }

        string specialtyName = await db.Specialties
            .AsNoTracking()
            .Where(s => s.Id == evt.SpecialtyId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? "Healthcare";

        // Derive booking reference from new appointment ID (same format as CreateBookingCommandHandler).
        string bookingReference = $"APT-{evt.NewAppointmentId.ToString("N")[..8].ToUpperInvariant()}";

        // Deserialize communication preferences; default to opted-in when field is absent.
        PatientCommunicationPreferences? prefs = null;
        if (!string.IsNullOrWhiteSpace(patient.CommunicationPreferencesJson))
        {
            try
            {
                prefs = JsonSerializer.Deserialize<PatientCommunicationPreferences>(
                    patient.CommunicationPreferencesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex,
                    "SwapNotification_PrefParseError: PatientId={PatientId} — defaulting to opted-in.",
                    evt.PatientId);
            }
        }

        // Step 2 — SMS eligibility check (AC-4).
        bool smsOptIn  = prefs?.SmsEnabled ?? true;
        bool hasPhone  = !string.IsNullOrWhiteSpace(patient.Phone);
        bool skipSms   = !smsOptIn || !hasPhone;
        string? skipReason = skipSms
            ? (!hasPhone ? "NoVerifiedPhone" : "SmsOptOut")
            : null;

        // Step 3 — Email dispatch.
        bool emailSuccess = false;
        DateTime? emailSentAt = null;
        var emailNotifId = Guid.NewGuid();

        try
        {
            emailSuccess = await _emailService.SendSlotSwapEmailAsync(
                toEmail:             patient.Email,
                patientName:         patient.Name,
                appointmentDate:     evt.NewDate,
                appointmentTimeStart: evt.NewTimeSlotStart,
                specialtyName:       specialtyName,
                bookingReference:    bookingReference,
                cancellationToken:   ct);

            emailSentAt = emailSuccess ? DateTime.UtcNow : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SwapNotification_EmailDispatchError: NewAppointmentId={NewAppointmentId}",
                evt.NewAppointmentId);
        }

        await InsertNotificationAsync(
            id:          emailNotifId,
            patientId:   evt.PatientId,
            appointmentId: evt.NewAppointmentId,
            channel:     NotificationChannel.Email,
            template:    EmailTemplate,
            success:     emailSuccess,
            sentAt:      emailSentAt,
            errorMessage: emailSuccess ? null : "Email delivery failed — see Serilog for details.",
            ct);

        await AppendAuditLogAsync(evt.PatientId, emailNotifId, "NotificationDispatched", ct);

        // Step 4 — SMS dispatch or skip record.
        bool smsSuccess = false;
        DateTime? smsSentAt = null;
        var smsNotifId = Guid.NewGuid();

        if (skipSms)
        {
            // AC-4: Insert a skip record so the audit trail captures the reason.
            await InsertNotificationAsync(
                id:          smsNotifId,
                patientId:   evt.PatientId,
                appointmentId: evt.NewAppointmentId,
                channel:     NotificationChannel.Sms,
                template:    SmsSkipTemplate,
                success:     false,
                sentAt:      null,
                errorMessage: skipReason,
                ct);

            _logger.LogInformation(
                "SwapNotification_SmsSkipped: PatientId={PatientId} Reason={Reason}",
                evt.PatientId, skipReason);
        }
        else
        {
            try
            {
                var smsResult = await _smsService.SendSlotSwapSmsAsync(
                    toPhoneNumber:        patient.Phone,
                    appointmentDate:      evt.NewDate,
                    appointmentTimeStart: evt.NewTimeSlotStart,
                    specialtyName:        specialtyName,
                    bookingReference:     bookingReference,
                    cancellationToken:    ct);

                smsSuccess = smsResult.Success;
                smsSentAt  = smsResult.DeliveryTimestamp;

                await InsertNotificationAsync(
                    id:          smsNotifId,
                    patientId:   evt.PatientId,
                    appointmentId: evt.NewAppointmentId,
                    channel:     NotificationChannel.Sms,
                    template:    SmsTemplate,
                    success:     smsSuccess,
                    sentAt:      smsSentAt,
                    errorMessage: smsSuccess ? null : smsResult.ErrorMessage,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SwapNotification_SmsDispatchError: NewAppointmentId={NewAppointmentId}",
                    evt.NewAppointmentId);

                await InsertNotificationAsync(
                    id:          smsNotifId,
                    patientId:   evt.PatientId,
                    appointmentId: evt.NewAppointmentId,
                    channel:     NotificationChannel.Sms,
                    template:    SmsTemplate,
                    success:     false,
                    sentAt:      null,
                    errorMessage: "SMS dispatch threw an unexpected exception.",
                    ct);
            }

            await AppendAuditLogAsync(evt.PatientId, smsNotifId, "NotificationDispatched", ct);
        }

        // Structured log for observability — no PHI values (NFR-013, HIPAA).
        _logger.LogInformation(
            "SwapNotification_Dispatched: PatientId={PatientId} NewAppointmentId={NewAppointmentId} " +
            "EmailStatus={EmailStatus} SmsStatus={SmsStatus}",
            evt.PatientId, evt.NewAppointmentId,
            emailSuccess ? "Sent" : "Failed",
            skipSms ? "Skipped" : (smsSuccess ? "Sent" : "Failed"));

        // Step 5 — Dual-failure flag: both email and SMS failed (not skipped = actual delivery attempt).
        if (!emailSuccess && !skipSms && !smsSuccess)
        {
            await FlagDualFailureAsync(db, evt, bookingReference, ct);
        }
    }

    /// <summary>Inserts a <see cref="Notification"/> row for a single channel dispatch result.</summary>
    private async Task InsertNotificationAsync(
        Guid id,
        Guid patientId,
        Guid appointmentId,
        NotificationChannel channel,
        string template,
        bool success,
        DateTime? sentAt,
        string? errorMessage,
        CancellationToken ct)
    {
        try
        {
            await _notificationRepository.InsertAsync(new Notification
            {
                Id           = id,
                PatientId    = patientId,
                AppointmentId = appointmentId,
                Channel      = channel,
                TemplateType = template,
                Status       = success ? NotificationStatus.Sent : NotificationStatus.Failed,
                SentAt       = sentAt,
                RetryCount   = 0,
                ErrorMessage = errorMessage,
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            // Notification record persistence failure is logged but must not halt the pipeline.
            _logger.LogError(ex,
                "SwapNotification_InsertFailed: NotificationId={NotificationId} Channel={Channel}",
                id, channel);
        }
    }

    /// <summary>Appends an immutable audit log entry for a dispatched notification (FR-034, AD-7).</summary>
    private async Task AppendAuditLogAsync(
        Guid patientId,
        Guid notificationId,
        string action,
        CancellationToken ct)
    {
        try
        {
            // No PHI (email address, phone number) in the Details JSONB payload (NFR-013).
            await _auditLogRepository.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                PatientId  = patientId,
                Role       = "System",
                Action     = action,
                EntityType = nameof(Notification),
                EntityId   = notificationId,
                Timestamp  = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SwapNotification_AuditLogFailed: NotificationId={NotificationId}",
                notificationId);
        }
    }

    /// <summary>
    /// Appends a <c>SwapNotificationFailure</c> alert to <c>Patient.PendingAlertsJson</c>
    /// when both email and SMS delivery fail (edge case — dual-failure dashboard flag).
    /// Uses an isolated DbContext scope so the alert write is independent of any other transaction.
    /// </summary>
    private async Task FlagDualFailureAsync(
        AppDbContext db,
        SlotSwapCompletedEvent evt,
        string bookingReference,
        CancellationToken ct)
    {
        try
        {
            // Re-fetch patient with tracking (the existing db scope has AsNoTracking above).
            await using var trackingDb = await _dbContextFactory.CreateDbContextAsync(ct);
            var trackedPatient = await trackingDb.Patients
                .FirstOrDefaultAsync(p => p.Id == evt.PatientId, ct);

            if (trackedPatient is null) return;

            // Build the new alert entry.
            var newAlert = new
            {
                alertType     = "SwapNotificationFailure",
                appointmentId = evt.NewAppointmentId,
                bookingReference,
                createdAt     = DateTime.UtcNow
            };

            // Deserialize existing alerts (or start with empty array), append, re-serialize.
            List<object> alerts = [];
            if (!string.IsNullOrWhiteSpace(trackedPatient.PendingAlertsJson))
            {
                try
                {
                    alerts = JsonSerializer.Deserialize<List<object>>(
                        trackedPatient.PendingAlertsJson) ?? [];
                }
                catch (JsonException)
                {
                    alerts = [];
                }
            }

            alerts.Add(newAlert);
            trackedPatient.PendingAlertsJson = JsonSerializer.Serialize(alerts);

            await trackingDb.SaveChangesAsync(ct);

            _logger.LogWarning(
                "SwapNotification_DualFailure: PatientId={PatientId} NewAppointmentId={NewAppointmentId} " +
                "— PendingAlert SwapNotificationFailure written.",
                evt.PatientId, evt.NewAppointmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SwapNotification_DualFailureFlagFailed: PatientId={PatientId}",
                evt.PatientId);
        }
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Commands;

/// <summary>
/// MediatR command that updates the reminder interval configuration and recalculates
/// all Pending <see cref="Propel.Domain.Entities.Notification"/> records for future appointments
/// (US_033, AC-3).
/// </summary>
public sealed record UpdateReminderIntervalsCommand(
    int[]   IntervalHours,
    Guid    RequestedByUserId,
    string? IpAddress,
    string? CorrelationId
) : IRequest<ReminderSettingsDto>;

/// <summary>
/// Handles <see cref="UpdateReminderIntervalsCommand"/>.
/// <para>
/// <b>Logic:</b>
/// <list type="number">
///   <item>Persist new intervals to <c>system_settings</c>.</item>
///   <item>Load all Pending <see cref="Propel.Domain.Entities.Notification"/> records for future appointments.</item>
///   <item>Delete records whose interval no longer exists in the new configuration.</item>
///   <item>Update <c>ScheduledAt</c> for records whose interval remains.</item>
///   <item>Create Email + SMS Notification records for newly added intervals (for all future Booked appointments).</item>
///   <item>Append audit log entry with before/after interval values (NFR-009).</item>
/// </list>
/// Idempotent: if the same interval set is submitted, no Notification records are modified.
/// </para>
/// </summary>
public sealed class UpdateReminderIntervalsCommandHandler
    : IRequestHandler<UpdateReminderIntervalsCommand, ReminderSettingsDto>
{
    private readonly ISystemSettingsRepository     _settingsRepo;
    private readonly INotificationRepository       _notifRepo;
    private readonly IAppointmentReminderRepository _apptRepo;
    private readonly IAuditLogRepository           _auditLogRepo;
    private readonly ILogger<UpdateReminderIntervalsCommandHandler> _logger;

    public UpdateReminderIntervalsCommandHandler(
        ISystemSettingsRepository     settingsRepo,
        INotificationRepository       notifRepo,
        IAppointmentReminderRepository apptRepo,
        IAuditLogRepository           auditLogRepo,
        ILogger<UpdateReminderIntervalsCommandHandler> logger)
    {
        _settingsRepo = settingsRepo;
        _notifRepo    = notifRepo;
        _apptRepo     = apptRepo;
        _auditLogRepo = auditLogRepo;
        _logger       = logger;
    }

    public async Task<ReminderSettingsDto> Handle(
        UpdateReminderIntervalsCommand request,
        CancellationToken cancellationToken)
    {
        var previousIntervals = await _settingsRepo.GetReminderIntervalsAsync(cancellationToken);

        // Idempotency: if the interval set is unchanged, skip all DB writes (AC-3).
        if (SameSet(previousIntervals, request.IntervalHours))
        {
            _logger.LogInformation(
                "ReminderSettings_NoChange: submitted intervals match current configuration [{Intervals}].",
                string.Join(",", request.IntervalHours));
            return new ReminderSettingsDto(request.IntervalHours);
        }

        // 1. Persist new interval configuration.
        await _settingsRepo.SetReminderIntervalsAsync(
            request.IntervalHours,
            request.RequestedByUserId,
            cancellationToken);

        // 2. Recalculate Pending Notification records for future appointments.
        var pendingRecords = await _notifRepo.GetPendingForFutureAppointmentsAsync(cancellationToken);

        foreach (var record in pendingRecords)
        {
            var intervalHours = ExtractIntervalHours(record.TemplateType);
            if (intervalHours is null)
                continue;

            if (!request.IntervalHours.Contains(intervalHours.Value))
            {
                // Interval removed — discard the pending notification.
                await _notifRepo.DeleteAsync(record.NotificationId, cancellationToken);
            }
            else
            {
                // Interval retained — recalculate scheduledAt with the appointment's current start time.
                var newScheduledAt = record.AppointmentStartUtc.AddHours(-intervalHours.Value);
                await _notifRepo.UpdateScheduledAtAsync(record.NotificationId, newScheduledAt, cancellationToken);
            }
        }

        // 3. Create Notification records for newly added intervals.
        var addedIntervals = request.IntervalHours.Except(previousIntervals).ToArray();
        if (addedIntervals.Length > 0)
        {
            var futureAppointments = await _apptRepo.GetBookedFutureAppointmentsAsync(cancellationToken);
            var utcNow = DateTime.UtcNow;

            foreach (var appointment in futureAppointments)
            {
                if (appointment.PatientId is null || appointment.TimeSlotStart is null)
                    continue;

                var appointmentStartUtc = new DateTime(
                    appointment.Date.Year, appointment.Date.Month, appointment.Date.Day,
                    appointment.TimeSlotStart.Value.Hour, appointment.TimeSlotStart.Value.Minute, 0,
                    DateTimeKind.Utc);

                foreach (var newInterval in addedIntervals)
                {
                    var scheduledAt = appointmentStartUtc.AddHours(-newInterval);

                    // Skip windows already in the past.
                    if (scheduledAt <= utcNow)
                        continue;

                    var emailTemplateType = $"Reminder_{newInterval}h_Email";
                    if (!await _notifRepo.ExistsAsync(appointment.Id, emailTemplateType, scheduledAt, cancellationToken))
                    {
                        await _notifRepo.InsertAsync(new Domain.Entities.Notification
                        {
                            Id            = Guid.NewGuid(),
                            AppointmentId = appointment.Id,
                            PatientId     = appointment.PatientId.Value,
                            Channel       = NotificationChannel.Email,
                            TemplateType  = emailTemplateType,
                            Status        = NotificationStatus.Pending,
                            ScheduledAt   = scheduledAt,
                            CreatedAt     = utcNow,
                            UpdatedAt     = utcNow,
                            RetryCount    = 0
                        }, cancellationToken);
                    }

                    var smsTemplateType = $"Reminder_{newInterval}h_Sms";
                    if (!await _notifRepo.ExistsAsync(appointment.Id, smsTemplateType, scheduledAt, cancellationToken))
                    {
                        await _notifRepo.InsertAsync(new Domain.Entities.Notification
                        {
                            Id            = Guid.NewGuid(),
                            AppointmentId = appointment.Id,
                            PatientId     = appointment.PatientId.Value,
                            Channel       = NotificationChannel.Sms,
                            TemplateType  = smsTemplateType,
                            Status        = NotificationStatus.Pending,
                            ScheduledAt   = scheduledAt,
                            CreatedAt     = utcNow,
                            UpdatedAt     = utcNow,
                            RetryCount    = 0
                        }, cancellationToken);
                    }
                }
            }
        }

        // 4. Append audit log entry with before/after interval values (NFR-009).
        await AppendAuditLogAsync(request, previousIntervals, cancellationToken);

        _logger.LogInformation(
            "ReminderSettings_Updated: before=[{Before}] after=[{After}] by={UserId}",
            string.Join(",", previousIntervals),
            string.Join(",", request.IntervalHours),
            request.RequestedByUserId);

        return new ReminderSettingsDto(request.IntervalHours);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the integer interval hours from a template type such as
    /// <c>"Reminder_48h_Email"</c> or <c>"Reminder_2h_Sms"</c>.
    /// Returns <c>null</c> if the template type does not match the expected format.
    /// </summary>
    private static int? ExtractIntervalHours(string templateType)
    {
        // Expected format: "Reminder_{N}h_{Channel}"
        const string prefix = "Reminder_";
        const string suffix_marker = "h_";

        if (!templateType.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var afterPrefix = templateType.AsSpan(prefix.Length);
        var markerIdx   = afterPrefix.IndexOf(suffix_marker, StringComparison.Ordinal);
        if (markerIdx <= 0)
            return null;

        var numericPart = afterPrefix[..markerIdx];
        return int.TryParse(numericPart, out var hours) ? hours : null;
    }

    /// <summary>
    /// Returns <c>true</c> if both arrays contain the same set of values (order-independent).
    /// </summary>
    private static bool SameSet(int[] a, int[] b)
        => a.Length == b.Length && a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x));

    private async Task AppendAuditLogAsync(
        UpdateReminderIntervalsCommand request,
        int[] previousIntervals,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before = previousIntervals,
                after  = request.IntervalHours
            }));

            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id            = Guid.NewGuid(),
                UserId        = request.RequestedByUserId,
                Action        = "ReminderIntervalsUpdated",
                EntityType    = "SystemSettings",
                EntityId      = Guid.Empty,
                Details       = details,
                IpAddress     = request.IpAddress,
                CorrelationId = request.CorrelationId,
                Timestamp     = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit failure must not interrupt the successful update response (NFR-018 graceful degradation).
            _logger.LogError(ex, "ReminderSettings_AuditLog_Failed: could not append audit entry.");
        }
    }
}

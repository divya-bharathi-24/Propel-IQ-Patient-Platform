using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Audit;
using Propel.Modules.Notification.Dispatchers;
using Propel.Modules.Notification.Services;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Periodic background service that evaluates upcoming appointments and creates persisted
/// <see cref="Domain.Entities.Notification"/> reminder jobs for the 48h, 24h, and 2h windows
/// (US_033, AC-1, AC-4, Edge Case 2).
/// <para>
/// <b>Poll interval:</b> 5 minutes (<see cref="PeriodicTimer"/> — no drift accumulation).
/// </para>
/// <para>
/// <b>Startup resume:</b> On startup, before the periodic loop begins, all Pending Notification
/// records with <c>scheduledAt &lt;= utcNow</c> are dispatched immediately to guarantee
/// at-least-once delivery across service restarts (US_033, Edge Case 2).
/// </para>
/// <para>
/// <b>Idempotency:</b> Before creating a Notification record, <see cref="INotificationRepository.ExistsAsync"/>
/// verifies no record with the same <c>(appointmentId, templateType, scheduledAt)</c> triple
/// already exists (AC-1). Duplicate job creation is prevented without needing distributed locks.
/// </para>
/// <para>
/// <b>Suppression:</b> If an appointment is Cancelled at evaluation time, all pending future
/// Notification records are marked <c>Suppressed</c> with <c>suppressedAt = utcNow</c> and
/// a suppression event is written to the audit log (AC-4, TR-018, NFR-009).
/// </para>
/// <para>
/// <b>Interval caching:</b> Reminder interval configuration is read from <see cref="ISystemSettingsRepository"/>
/// and cached in-process for <see cref="IntervalCacheDuration"/> to avoid a DB round-trip on
/// every 5-minute tick while still reacting to configuration changes within 5 minutes (FR-032).
/// </para>
/// <para>
/// <b>Scoped DI:</b> All scoped services are resolved via <see cref="IServiceScopeFactory"/>
/// per tick to avoid captive-dependency issues with the singleton-lifetime <see cref="BackgroundService"/> (AD-1).
/// </para>
/// No PHI is written to Serilog log values (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class ReminderSchedulerService : BackgroundService, IReminderSchedulerService
{
    private static readonly TimeSpan PollInterval        = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IntervalCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderSchedulerService> _logger;

    // In-process cache for reminder intervals to avoid a DB round-trip on every tick (FR-032).
    private int[]?   _cachedIntervals;
    private DateTime _cacheExpiresAt = DateTime.MinValue;

    public ReminderSchedulerService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReminderSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ReminderScheduler_Started: poll interval = {PollIntervalMin} min.",
            PollInterval.TotalMinutes);

        // Resume any Pending reminders that are already due on startup (at-least-once delivery).
        await ResumeIncompleteJobsAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EvaluateAndQueueRemindersAsync(stoppingToken);
        }

        _logger.LogInformation("ReminderScheduler_Stopped.");
    }

    // ── Startup resume ────────────────────────────────────────────────────────

    private async Task ResumeIncompleteJobsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope      = _scopeFactory.CreateAsyncScope();
            var sp         = scope.ServiceProvider;
            var notifRepo  = sp.GetRequiredService<INotificationRepository>();
            var dispatcher = sp.GetRequiredService<INotificationDispatcher>();

            var due = await notifRepo.GetPendingDueAsync(ct);
            if (due.Count == 0)
            {
                _logger.LogInformation("ReminderScheduler_ResumeOnStartup: no overdue jobs found.");
                return;
            }

            _logger.LogInformation(
                "ReminderScheduler_ResumeOnStartup: {Count} overdue Pending jobs dispatching immediately.",
                due.Count);

            // Dispatch each overdue job immediately to guarantee at-least-once delivery (Edge Case 2).
            foreach (var notification in due)
            {
                if (ct.IsCancellationRequested) break;
                await dispatcher.DispatchAsync(notification, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — suppress.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReminderScheduler_ResumeOnStartup_Failed: error resuming overdue jobs.");
        }
    }

    // ── Main tick ─────────────────────────────────────────────────────────────

    private async Task EvaluateAndQueueRemindersAsync(CancellationToken ct)
    {
        var tickStart = DateTime.UtcNow;
        _logger.LogInformation("ReminderScheduler_TickStart: {UtcNow:O}", tickStart);

        try
        {
            await using var scope       = _scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            var intervals    = await GetCachedIntervalsAsync(sp, ct);
            var apptRepo     = sp.GetRequiredService<IAppointmentReminderRepository>();
            var notifRepo    = sp.GetRequiredService<INotificationRepository>();
            var auditLogRepo = sp.GetRequiredService<IAuditLogRepository>();

            var appointments = await apptRepo.GetAppointmentsForReminderEvaluationAsync(intervals, cancellationToken: ct);

            int created     = 0;
            int suppressed  = 0;

            foreach (var appt in appointments)
            {
                if (ct.IsCancellationRequested) break;

                if (appt.Status == AppointmentStatus.Cancelled)
                {
                    int count = await SuppressAndLogAsync(appt.Id, notifRepo, auditLogRepo, ct);
                    suppressed += count;
                    continue;
                }

                // Appointment is Booked — create Email + SMS Notification records for each interval window.
                // Two separate records per channel allow independent status tracking (DR-015, task_002).
                foreach (var intervalHours in intervals)
                {
                    var appointmentStartUtc = AppointmentStartUtc(appt);
                    if (appointmentStartUtc is null) continue;

                    var scheduledAt = appointmentStartUtc.Value.AddHours(-intervalHours);

                    // Skip windows already in the past.
                    if (scheduledAt <= DateTime.UtcNow) continue;

                    var utcNow = DateTime.UtcNow;

                    // Email record — idempotency check uses channel-namespaced templateType.
                    var emailTemplateType = $"Reminder_{intervalHours}h_Email";
                    if (!await notifRepo.ExistsAsync(appt.Id, emailTemplateType, scheduledAt, ct))
                    {
                        await notifRepo.InsertAsync(new Domain.Entities.Notification
                        {
                            Id            = Guid.NewGuid(),
                            AppointmentId = appt.Id,
                            PatientId     = appt.PatientId!.Value,
                            Channel       = NotificationChannel.Email,
                            TemplateType  = emailTemplateType,
                            Status        = NotificationStatus.Pending,
                            ScheduledAt   = scheduledAt,
                            CreatedAt     = utcNow,
                            UpdatedAt     = utcNow,
                            RetryCount    = 0
                        }, ct);
                        created++;
                    }

                    // SMS record — idempotency check uses channel-namespaced templateType.
                    var smsTemplateType = $"Reminder_{intervalHours}h_Sms";
                    if (!await notifRepo.ExistsAsync(appt.Id, smsTemplateType, scheduledAt, ct))
                    {
                        await notifRepo.InsertAsync(new Domain.Entities.Notification
                        {
                            Id            = Guid.NewGuid(),
                            AppointmentId = appt.Id,
                            PatientId     = appt.PatientId!.Value,
                            Channel       = NotificationChannel.Sms,
                            TemplateType  = smsTemplateType,
                            Status        = NotificationStatus.Pending,
                            ScheduledAt   = scheduledAt,
                            CreatedAt     = utcNow,
                            UpdatedAt     = utcNow,
                            RetryCount    = 0
                        }, ct);
                        created++;
                    }
                }
            }

            _logger.LogInformation(
                "ReminderScheduler_TickEnd: appointments={ApptCount} created={Created} suppressed={Suppressed} elapsed={ElapsedMs}ms",
                appointments.Count,
                created,
                suppressed,
                (int)(DateTime.UtcNow - tickStart).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — suppress cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReminderScheduler_TickFailed: unhandled error during reminder evaluation.");
        }
    }

    // ── Suppression ───────────────────────────────────────────────────────────

    private async Task<int> SuppressAndLogAsync(
        Guid appointmentId,
        INotificationRepository notifRepo,
        IAuditLogRepository auditLogRepo,
        CancellationToken ct)
    {
        int count = await notifRepo.SuppressPendingByAppointmentAsync(appointmentId, ct);
        if (count == 0) return 0;

        _logger.LogInformation(
            "ReminderScheduler_Suppressed: appointmentId={AppointmentId} count={Count}",
            appointmentId,
            count);

        // Write suppression event to audit log (US_033, AC-4, TR-018).
        try
        {
            await auditLogRepo.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                UserId     = null,        // system-initiated; no authenticated user
                PatientId  = null,        // PatientId not carried on Notification — no PHI in audit payload
                Role       = "System",
                Action     = "ReminderSuppressed",
                EntityType = "Notification",
                EntityId   = appointmentId,
                IpAddress  = null,
                CorrelationId = null,
                Timestamp  = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            // Audit failure must never interrupt scheduler processing (OWASP A09).
            _logger.LogWarning(ex,
                "ReminderScheduler_AuditWriteFailed: suppression audit entry could not be persisted for appointment {AppointmentId}.",
                appointmentId);
        }

        return count;
    }

    // ── Interval cache ────────────────────────────────────────────────────────

    private async Task<int[]> GetCachedIntervalsAsync(IServiceProvider sp, CancellationToken ct)
    {
        if (_cachedIntervals is not null && DateTime.UtcNow < _cacheExpiresAt)
            return _cachedIntervals;

        var settingsRepo   = sp.GetRequiredService<ISystemSettingsRepository>();
        _cachedIntervals   = await settingsRepo.GetReminderIntervalsAsync(ct);
        _cacheExpiresAt    = DateTime.UtcNow.Add(IntervalCacheDuration);

        _logger.LogInformation(
            "ReminderScheduler_IntervalsLoaded: intervals=[{Intervals}]",
            string.Join(",", _cachedIntervals));

        return _cachedIntervals;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Combines <see cref="Appointment.Date"/> and <see cref="Appointment.TimeSlotStart"/>
    /// into a UTC <see cref="DateTime"/>. Returns <c>null</c> for unscheduled appointments.
    /// </summary>
    private static DateTime? AppointmentStartUtc(Appointment a)
    {
        if (a.TimeSlotStart is null) return null;

        return new DateTime(
            a.Date.Year, a.Date.Month, a.Date.Day,
            a.TimeSlotStart.Value.Hour, a.TimeSlotStart.Value.Minute, 0,
            DateTimeKind.Utc);
    }
}

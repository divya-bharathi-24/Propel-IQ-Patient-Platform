using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Notifiers;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Long-running <see cref="BackgroundService"/> that retries failed booking-confirmation
/// and ad-hoc notification deliveries with exponential backoff (US_052, AC-2, NFR-018).
/// <para>
/// <b>Poll interval:</b> 60 seconds. On each tick, all <c>Notification</c> records that
/// satisfy the backoff guard (<c>4^retryCount</c> minutes elapsed since <c>LastRetryAt</c>)
/// are attempted. Notifications that succeed are marked <c>Sent</c>; failures increment
/// <c>RetryCount</c>. When <c>RetryCount</c> reaches <see cref="MaxRetries"/> the record is
/// permanently marked <c>Failed</c> and a Serilog Warning is emitted — no further automatic
/// retry is scheduled (manual resend is handled by a future admin workflow).
/// </para>
/// <para>
/// <b>Scope:</b> <see cref="INotificationRepository"/>, <see cref="IEmailNotifier"/>,
/// <see cref="ISmsNotifier"/>, <see cref="IPatientRepository"/>, and
/// <see cref="IAppointmentBookingRepository"/> are all scoped services; they are resolved
/// per poll cycle via <see cref="IServiceScopeFactory"/> to satisfy the .NET DI
/// captive-dependency constraint (singleton <c>BackgroundService</c> → scoped dependency).
/// </para>
/// <para>
/// <b>Record filter:</b> Only records with <c>ScheduledAt IS NULL</c> are processed here;
/// scheduler-managed reminder records (which carry a non-null <c>ScheduledAt</c>) are handled
/// exclusively by <c>ReminderSchedulerService</c> and <c>INotificationDispatcher</c>.
/// </para>
/// No PHI is written to Serilog structured log properties (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class NotificationRetryBackgroundService : BackgroundService
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationRetryBackgroundService> _logger;

    public NotificationRetryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationRetryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NotificationRetryJob_Started: poll interval = {PollIntervalSec}s, maxRetries = {MaxRetries}.",
            (int)LoopInterval.TotalSeconds, MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LoopInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ProcessPendingNotificationsAsync(stoppingToken);
        }

        _logger.LogInformation("NotificationRetryJob_Stopped.");
    }

    /// <summary>
    /// Queries all <c>Pending</c> booking-notification records and applies the exponential
    /// backoff filter in-memory before dispatching each due record.
    /// All errors for the batch are caught so a single bad record never blocks the rest.
    /// </summary>
    private async Task ProcessPendingNotificationsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

            IReadOnlyList<Notification> pending =
                await notificationRepo.GetRetryEligibleBookingNotificationsAsync(MaxRetries, ct);

            if (pending.Count == 0)
                return;

            var utcNow = DateTime.UtcNow;

            // Apply exponential backoff filter in-memory:
            //   Attempt 1 (retryCount=0): 4^0 = 1 minute
            //   Attempt 2 (retryCount=1): 4^1 = 4 minutes
            //   Attempt 3 (retryCount=2): 4^2 = 16 minutes
            var due = pending.Where(n =>
            {
                if (n.LastRetryAt is null)
                    return true;

                double backoffMinutes = Math.Pow(4, n.RetryCount);
                return (utcNow - n.LastRetryAt.Value).TotalMinutes >= backoffMinutes;
            }).ToList();

            _logger.LogInformation(
                "NotificationRetryJob_Tick: {PendingCount} pending, {DueCount} due for retry.",
                pending.Count, due.Count);

            foreach (var notification in due)
            {
                await AttemptRetryAsync(notification, scope, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutting down — exit cleanly.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NotificationRetryJob_BatchError: unexpected error during retry batch.");
        }
    }

    /// <summary>
    /// Attempts delivery for a single notification, resolving contact and appointment data
    /// from the service scope. Updates the notification record with the outcome.
    /// </summary>
    private async Task AttemptRetryAsync(
        Notification notification,
        AsyncServiceScope scope,
        CancellationToken ct)
    {
        try
        {
            var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var patientRepo      = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
            var appointmentRepo  = scope.ServiceProvider.GetRequiredService<IAppointmentBookingRepository>();
            var emailNotifier    = scope.ServiceProvider.GetRequiredService<IEmailNotifier>();
            var smsNotifier      = scope.ServiceProvider.GetRequiredService<ISmsNotifier>();

            // Resolve patient contact details (no PHI in log values — NFR-013).
            var patient = await patientRepo.GetCommunicationPreferencesAsync(
                notification.PatientId, ct);

            if (patient is null)
            {
                await PermanentlyFailAsync(notification, notificationRepo,
                    "Patient record not found during retry.", ct);
                return;
            }

            // Resolve appointment for payload construction.
            Appointment? appointment = notification.AppointmentId.HasValue
                ? await appointmentRepo.GetByIdWithRelatedAsync(notification.AppointmentId.Value, ct)
                : null;

            if (appointment is null || appointment.TimeSlotStart is null)
            {
                await PermanentlyFailAsync(notification, notificationRepo,
                    "Appointment not found or has no time slot during retry.", ct);
                return;
            }

            string? specialtyName = await appointmentRepo.GetSpecialtyNameAsync(
                appointment.SpecialtyId, ct);

            string referenceNumber =
                $"APT-{notification.AppointmentId!.Value.ToString("N")[..8].ToUpperInvariant()}";

            var payload = new ReminderPayload(
                PatientName:         patient.Value.Name,
                AppointmentDate:     appointment.Date,
                AppointmentTimeSlot: appointment.TimeSlotStart.Value,
                ProviderSpecialty:   specialtyName ?? "General",
                ReferenceNumber:     referenceNumber);

            NotifierResult result = notification.Channel == NotificationChannel.Email
                ? await emailNotifier.SendAsync(patient.Value.Email, payload, ct)
                : await smsNotifier.SendAsync(patient.Value.Phone, payload, ct);

            var utcNow = DateTime.UtcNow;

            if (result.IsSuccess)
            {
                notification.Status    = NotificationStatus.Sent;
                notification.SentAt    = utcNow;
                notification.UpdatedAt = utcNow;
                await notificationRepo.UpdateAsync(notification, ct);
                return;
            }

            // Delivery failed — increment retry counter.
            notification.RetryCount++;
            notification.LastRetryAt = utcNow;
            notification.UpdatedAt   = utcNow;

            if (notification.RetryCount >= MaxRetries)
            {
                notification.Status       = NotificationStatus.Failed;
                notification.ErrorMessage = result.ErrorMessage;
                _logger.LogWarning(
                    "NotificationRetryJob_PermanentFail: notification {NotifId} failed permanently " +
                    "after {RetryCount} attempts. Error: {Error}",
                    notification.Id, notification.RetryCount, result.ErrorMessage);
            }

            await notificationRepo.UpdateAsync(notification, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "NotificationRetryJob_AttemptError: notification {NotifId} retry attempt threw unexpectedly.",
                notification.Id);

            // Best-effort counter increment so we don't retry a consistently broken record indefinitely.
            try
            {
                await using var fallbackScope = _scopeFactory.CreateAsyncScope();
                var fallbackRepo = fallbackScope.ServiceProvider.GetRequiredService<INotificationRepository>();

                // Re-fetch to get a tracked-free copy for the fallback update.
                var candidates = await fallbackRepo.GetRetryEligibleBookingNotificationsAsync(MaxRetries, ct);
                var fresh = candidates.FirstOrDefault(n => n.Id == notification.Id);
                if (fresh is not null)
                {
                    fresh.RetryCount++;
                    fresh.LastRetryAt = DateTime.UtcNow;
                    fresh.UpdatedAt   = DateTime.UtcNow;
                    if (fresh.RetryCount >= MaxRetries)
                    {
                        fresh.Status       = NotificationStatus.Failed;
                        fresh.ErrorMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
                        _logger.LogWarning(
                            "NotificationRetryJob_PermanentFail: notification {NotifId} failed permanently " +
                            "after {RetryCount} attempts (exception path).",
                            fresh.Id, fresh.RetryCount);
                    }

                    await fallbackRepo.UpdateAsync(fresh, ct);
                }
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx,
                    "NotificationRetryJob_FallbackUpdateFailed: could not persist retry counter for {NotifId}.",
                    notification.Id);
            }
        }
    }

    private static async Task PermanentlyFailAsync(
        Notification notification,
        INotificationRepository repo,
        string reason,
        CancellationToken ct)
    {
        notification.Status       = NotificationStatus.Failed;
        notification.ErrorMessage = reason;
        notification.UpdatedAt    = DateTime.UtcNow;
        await repo.UpdateAsync(notification, ct);
    }
}

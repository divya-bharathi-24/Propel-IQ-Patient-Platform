using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="INotificationRepository"/> (US_025, US_033).
/// Uses <see cref="IDbContextFactory{TContext}"/> to create an isolated <see cref="AppDbContext"/>
/// scope per write, consistent with the AD-7 non-request-scoped write pattern used by
/// <see cref="AuditLogRepository"/>. This ensures notification records are never rolled back
/// by an outer business transaction.
/// Scheduler query methods (<see cref="ExistsAsync"/>, <see cref="GetPendingDueAsync"/>,
/// <see cref="SuppressPendingByAppointmentAsync"/>) also use isolated factory scopes for
/// consistency and thread-safety within the BackgroundService (US_033, task_001).
/// </summary>
public sealed class NotificationRepository : INotificationRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public NotificationRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc/>
    public async Task<Guid> InsertAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
        return notification.Id;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        Guid appointmentId,
        string templateType,
        DateTime scheduledAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Notifications
            .AsNoTracking()
            .AnyAsync(
                n => n.AppointmentId == appointmentId
                  && n.TemplateType  == templateType
                  && n.ScheduledAt   == scheduledAt,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Notification>> GetPendingDueAsync(
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Notifications
            .AsNoTracking()
            .Where(n => n.Status == NotificationStatus.Pending
                     && n.ScheduledAt != null
                     && n.ScheduledAt <= utcNow)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> SuppressPendingByAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pendingFuture = await context.Notifications
            .Where(n => n.AppointmentId == appointmentId
                     && n.Status        == NotificationStatus.Pending
                     && n.ScheduledAt   != null
                     && n.ScheduledAt   > utcNow)
            .ToListAsync(cancellationToken);

        if (pendingFuture.Count == 0)
            return 0;

        foreach (var notification in pendingFuture)
        {
            notification.Status      = NotificationStatus.Suppressed;
            notification.SuppressedAt = utcNow;
            notification.UpdatedAt   = utcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        return pendingFuture.Count;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Attach the detached entity and mark only the mutable dispatch-outcome columns as modified
        // to avoid unintentional overwrites of unchanged columns (AD-7).
        context.Notifications.Attach(notification);
        var entry = context.Entry(notification);
        entry.Property(n => n.Status).IsModified       = true;
        entry.Property(n => n.SentAt).IsModified       = true;
        entry.Property(n => n.RetryCount).IsModified   = true;
        entry.Property(n => n.LastRetryAt).IsModified  = true;
        entry.Property(n => n.ErrorMessage).IsModified = true;
        entry.Property(n => n.UpdatedAt).IsModified    = true;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PendingReminderRecord>> GetPendingForFutureAppointmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // LINQ join: Notifications ⟶ Appointments to obtain appointment start time (AC-3).
        // IgnoreQueryFilters() bypasses the Cancelled soft-delete filter on Appointments so
        // pending reminders for cancelled appointments are also returned (edge-case cleanup).
        // Only Booked future appointments are relevant for recalculation; Cancelled will be
        // handled by suppression logic in the handler.
        // All queries are parameterised LINQ — no raw SQL interpolation (OWASP A03).
        var results = await (
            from n in context.Notifications
            join a in context.Appointments.IgnoreQueryFilters()
                on n.AppointmentId equals a.Id
            where n.Status        == NotificationStatus.Pending
               && n.AppointmentId != null
               && a.TimeSlotStart != null
            select new
            {
                n.Id,
                n.TemplateType,
                AppointmentId = a.Id,
                a.Date,
                TimeSlotStart = a.TimeSlotStart!.Value
            }
        ).ToListAsync(cancellationToken);

        // Filter in-memory: keep only appointments whose computed UTC start is in the future.
        return results
            .Select(r => new
            {
                r.Id,
                r.TemplateType,
                r.AppointmentId,
                AppointmentStartUtc = new DateTime(
                    r.Date.Year, r.Date.Month, r.Date.Day,
                    r.TimeSlotStart.Hour, r.TimeSlotStart.Minute, 0,
                    DateTimeKind.Utc)
            })
            .Where(r => r.AppointmentStartUtc > utcNow)
            .Select(r => new PendingReminderRecord(
                r.Id,
                r.TemplateType,
                r.AppointmentId,
                r.AppointmentStartUtc))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateScheduledAtAsync(
        Guid     notificationId,
        DateTime newScheduledAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var notification = await context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        if (notification is null)
            return;

        notification.ScheduledAt = newScheduledAt;
        notification.UpdatedAt   = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var notification = await context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        if (notification is null)
            return;

        context.Notifications.Remove(notification);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Notification?> GetLatestSentManualReminderAsync(
        Guid appointmentId,
        int withinMinutes,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-withinMinutes);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Parameterised LINQ — no raw SQL interpolation (OWASP A03).
        return await context.Notifications
            .AsNoTracking()
            .Where(n => n.AppointmentId == appointmentId
                     && n.TriggeredBy   != null
                     && n.Status        == NotificationStatus.Sent
                     && n.SentAt        >= cutoff)
            .OrderByDescending(n => n.SentAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Notification?> GetLatestManualReminderAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Parameterised LINQ — no raw SQL interpolation (OWASP A03).
        return await context.Notifications
            .AsNoTracking()
            .Where(n => n.AppointmentId == appointmentId
                     && n.TriggeredBy   != null
                     && n.Status        == NotificationStatus.Sent)
            .OrderByDescending(n => n.SentAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Notification>> GetRetryEligibleBookingNotificationsAsync(
        int maxRetries,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // ScheduledAt IS NULL distinguishes fire-and-try booking/confirmation records from
        // scheduler-managed reminder records (which always have a non-null ScheduledAt).
        // Parameterised LINQ — no raw SQL interpolation (OWASP A03).
        return await context.Notifications
            .AsNoTracking()
            .Where(n => n.Status       == NotificationStatus.Pending
                     && n.RetryCount    < maxRetries
                     && n.ScheduledAt  == null)
            .ToListAsync(cancellationToken);
    }
}

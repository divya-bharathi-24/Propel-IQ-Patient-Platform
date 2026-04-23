using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICalendarSyncRepository"/> (us_035, task_002).
/// All queries use parameterised LINQ — no raw string interpolation into SQL (OWASP A03).
/// </summary>
public sealed class CalendarSyncRepository : ICalendarSyncRepository
{
    private readonly AppDbContext _db;

    public CalendarSyncRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<CalendarSync?> GetAsync(
        Guid patientId,
        Guid appointmentId,
        CalendarProvider provider,
        CancellationToken cancellationToken = default)
    {
        return await _db.CalendarSyncs
            .FirstOrDefaultAsync(
                cs => cs.PatientId     == patientId
                   && cs.AppointmentId == appointmentId
                   && cs.Provider      == provider,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CalendarSync?> GetByAppointmentIdAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        // OWASP A01: always filter by patientId to prevent cross-patient access
        return await _db.CalendarSyncs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                cs => cs.AppointmentId == appointmentId
                   && cs.PatientId     == patientId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        CalendarSync calendarSync,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.CalendarSyncs
            .FirstOrDefaultAsync(
                cs => cs.PatientId     == calendarSync.PatientId
                   && cs.AppointmentId == calendarSync.AppointmentId
                   && cs.Provider      == calendarSync.Provider,
                cancellationToken);

        if (existing is null)
        {
            calendarSync.CreatedAt = DateTime.UtcNow;
            calendarSync.UpdatedAt = DateTime.UtcNow;
            await _db.CalendarSyncs.AddAsync(calendarSync, cancellationToken);
        }
        else
        {
            existing.ExternalEventId  = calendarSync.ExternalEventId;
            existing.EventLink        = calendarSync.EventLink;
            existing.SyncStatus       = calendarSync.SyncStatus;
            existing.SyncedAt         = calendarSync.SyncedAt;
            existing.ErrorMessage     = calendarSync.ErrorMessage;
            existing.RetryScheduledAt = calendarSync.RetryScheduledAt;
            existing.RetryCount       = calendarSync.RetryCount;
            existing.UpdatedAt        = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarSync>> GetFailedDueForRetryAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _db.CalendarSyncs
            .AsNoTracking()
            .Where(cs => cs.SyncStatus == CalendarSyncStatus.Failed
                      && cs.RetryScheduledAt != null
                      && cs.RetryScheduledAt <= now)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CalendarSync?> GetActiveByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _db.CalendarSyncs
            .FirstOrDefaultAsync(
                cs => cs.AppointmentId == appointmentId
                   && cs.SyncStatus    == CalendarSyncStatus.Synced,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateSyncStatusAsync(
        Guid id,
        CalendarSyncStatus status,
        DateTime? retryScheduledAt,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.CalendarSyncs
            .FirstOrDefaultAsync(cs => cs.Id == id, cancellationToken);

        if (record is null)
            return;

        record.SyncStatus        = status;
        record.RetryScheduledAt  = retryScheduledAt;
        record.ErrorMessage      = errorMessage;
        record.SyncedAt          = status == CalendarSyncStatus.Synced ? DateTime.UtcNow : record.SyncedAt;
        record.UpdatedAt         = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }
}

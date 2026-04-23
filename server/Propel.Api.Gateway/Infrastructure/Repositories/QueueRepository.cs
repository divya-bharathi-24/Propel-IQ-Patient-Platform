using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IQueueRepository"/> (US_027, task_002).
/// Provides read and write operations for the same-day appointment queue.
/// <para>
/// All queries use parameterised LINQ expressions — no raw string interpolation into SQL
/// (OWASP A03 — Injection Prevention).
/// </para>
/// </summary>
public sealed class QueueRepository : IQueueRepository
{
    private readonly AppDbContext _db;

    public QueueRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Appointment>> GetTodayAppointmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return await _db.Appointments
            .AsNoTracking()
            .IgnoreQueryFilters()   // Include Cancelled appointments in the queue view
            .Where(a => a.Date == today)
            .Include(a => a.Patient)
            .Include(a => a.QueueEntry)
            .OrderBy(a => a.TimeSlotStart)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Appointment?> GetAppointmentWithQueueEntryAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Appointments
            .IgnoreQueryFilters()   // Allow loading Cancelled appointments if needed
            .Include(a => a.QueueEntry)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}

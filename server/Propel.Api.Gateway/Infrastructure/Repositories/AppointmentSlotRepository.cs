using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAppointmentSlotRepository"/> (US_018, task_002).
/// <para>
/// Uses a single <c>AsNoTracking()</c> projection query to return only the
/// <c>(TimeSlotStart, TimeSlotEnd)</c> columns of active appointments, avoiding full entity
/// materialisation for the read-path (NFR-001, AD-2).
/// </para>
/// <para>
/// Only <c>Booked</c> and <c>Arrived</c> appointments are returned; <c>Cancelled</c> and
/// <c>Completed</c> appointments are excluded so cancelled slots become available for
/// re-booking immediately.
/// </para>
/// </summary>
public sealed class AppointmentSlotRepository : IAppointmentSlotRepository
{
    private readonly AppDbContext _db;

    public AppointmentSlotRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BookedSlotReadModel>> GetBookedSlotsAsync(
        Guid specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.SpecialtyId == specialtyId &&
                a.Date == date &&
                (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Arrived) &&
                a.TimeSlotStart.HasValue &&
                a.TimeSlotEnd.HasValue)
            .Select(a => new BookedSlotReadModel(a.TimeSlotStart!.Value, a.TimeSlotEnd!.Value))
            .ToListAsync(cancellationToken);
    }
}

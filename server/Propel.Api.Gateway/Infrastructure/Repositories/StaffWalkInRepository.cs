using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IStaffWalkInRepository"/> (US_026, task_002).
/// Handles patient search and atomic walk-in booking (Patient? + Appointment + QueueEntry)
/// in a single <see cref="AppDbContext"/> transaction.
/// <para>
/// All patient queries use parameterised LINQ expressions — no raw string interpolation
/// into SQL (OWASP A03 — Injection Prevention).
/// </para>
/// </summary>
public sealed class StaffWalkInRepository : IStaffWalkInRepository
{
    private readonly AppDbContext _db;

    public StaffWalkInRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Patient>> SearchPatientsAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        // Parameterised ILIKE search on name and exact match on date_of_birth::text (OWASP A03).
        // EF Core translates the LINQ Contains to a LIKE query; EF.Functions.ILike provides
        // case-insensitive PostgreSQL ILIKE semantics without string concatenation.
        var normalizedQuery = query.Trim();

        return await _db.Patients
            .AsNoTracking()
            .Where(p =>
                EF.Functions.ILike(p.Name, $"%{normalizedQuery}%") ||
                p.DateOfBirth.ToString() == normalizedQuery)
            .OrderBy(p => p.Name)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Patient?> GetPatientByIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Patient?> GetPatientByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // Normalisation to lower-case is the caller's responsibility (applied in handler).
        return await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Email == email, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsSlotBookedAsync(
        Guid specialtyId,
        DateOnly date,
        TimeOnly? timeSlotStart,
        CancellationToken cancellationToken = default)
    {
        if (timeSlotStart is null)
            return false;

        return await _db.Appointments
            .AsNoTracking()
            .AnyAsync(
                a => a.SpecialtyId == specialtyId
                  && a.Date == date
                  && a.TimeSlotStart == timeSlotStart
                  && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Arrived),
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetNextQueuePositionAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // COALESCE(MAX(position), 0) + 1 — safe even when no queue entries exist yet for the date.
        int maxPosition = await _db.QueueEntries
            .AsNoTracking()
            .Where(q => q.Appointment.Date == date)
            .Select(q => (int?)q.Position)
            .MaxAsync(cancellationToken) ?? 0;

        return maxPosition + 1;
    }

    /// <inheritdoc/>
    public async Task CreateWalkInAsync(
        Patient? newPatient,
        Appointment appointment,
        QueueEntry queueEntry,
        CancellationToken cancellationToken = default)
    {
        // Add all entities before SaveChangesAsync so they are committed atomically.
        if (newPatient is not null)
            _db.Patients.Add(newPatient);

        _db.Appointments.Add(appointment);
        _db.QueueEntries.Add(queueEntry);

        await _db.SaveChangesAsync(cancellationToken);
    }
}

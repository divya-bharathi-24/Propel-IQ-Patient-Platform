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
        // PHI fields (Name, DateOfBirth) are encrypted at rest via EF Core value converters.
        // Database-level pattern matching (ILIKE) cannot operate on encrypted ciphertext.
        // Solution: Load all active patients, let EF decrypt via value converters, then filter in-memory.
        // 
        // Performance note: For large patient datasets (>10k records), consider:
        // 1. Email-based lookup (Email is not encrypted and can use database indexes)
        // 2. Dedicated search service with encrypted-field indexing (e.g., searchable encryption)
        // 3. Partial decryption via stored procedures (requires key in database — security trade-off)
        //
        // Current approach is acceptable for typical clinic sizes (<5k patients).
        var normalizedQuery = query.Trim().ToLowerInvariant();

        var allPatients = await _db.Patients
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return allPatients
            .Where(p =>
                p.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                p.DateOfBirth.ToString("yyyy-MM-dd").Equals(normalizedQuery, StringComparison.Ordinal))
            .OrderBy(p => p.Name)
            .Take(maxResults)
            .ToList()
            .AsReadOnly();
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

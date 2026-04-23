using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Exceptions;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAppointmentBookingRepository"/> (US_019, task_002; US_020, task_002).
/// Handles all write operations for the appointment booking and cancellation flows.
/// <para>
/// <c>CreateAppointmentAsync</c> propagates <see cref="DbUpdateException"/> to callers so
/// that the command handler can detect unique partial index violations on
/// <c>(specialty_id, date, time_slot_start)</c> and map them to <c>SlotConflictException</c> (AC-3).
/// </para>
/// <para>
/// <c>GetByIdWithRelatedAsync</c> eager-loads Notifications, WaitlistEntry, and CalendarSync
/// in one round-trip query so the <c>CancelAppointmentCommandHandler</c> can mutate all
/// related records and commit atomically (US_020, AC-1, AC-2, AC-4).
/// </para>
/// </summary>
public sealed class AppointmentBookingRepository : IAppointmentBookingRepository
{
    private readonly AppDbContext _db;

    public AppointmentBookingRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<Appointment> CreateAppointmentAsync(
        Appointment appointment,
        CancellationToken cancellationToken = default)
    {
        _db.Appointments.Add(appointment);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new SlotConflictException("Slot no longer available");
        }
        return appointment;
    }

    /// <summary>
    /// Detects PostgreSQL unique constraint violations (SQLSTATE 23505).
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var innerType = ex.InnerException?.GetType().Name ?? string.Empty;
        var innerMessage = ex.InnerException?.Message ?? string.Empty;
        return innerType == "PostgresException" &&
               innerMessage.Contains("23505", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task CreateInsuranceValidationAsync(
        InsuranceValidation validation,
        CancellationToken cancellationToken = default)
    {
        _db.InsuranceValidations.Add(validation);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateWaitlistEntryAsync(
        WaitlistEntry entry,
        CancellationToken cancellationToken = default)
    {
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> GetSpecialtyNameAsync(
        Guid specialtyId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Specialties
            .AsNoTracking()
            .Where(s => s.Id == specialtyId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Appointment?> GetByIdWithRelatedAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Appointments
            .Include(a => a.Notifications.Where(n => n.Status == NotificationStatus.Pending))
            .Include(a => a.WaitlistEntry)
            .Include(a => a.CalendarSync)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Detects PostgreSQL unique constraint violations (SQLSTATE 23505) on the
    /// <c>(specialty_id, date, time_slot_start)</c> partial index and converts them to
    /// <see cref="SlotConflictException"/> so callers do not need an EF Core dependency.
    /// This is consistent with the pattern used in <see cref="CreateAppointmentAsync"/>.
    /// </remarks>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new SlotConflictException("Slot no longer available");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsSlotTakenAsync(
        Guid specialtyId,
        DateOnly date,
        TimeOnly timeSlotStart,
        CancellationToken cancellationToken = default)
    {
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
    public void StageAppointment(Appointment appointment)
    {
        _db.Appointments.Add(appointment);
    }

    /// <inheritdoc/>
    public async Task<Appointment?> GetByIdWithPatientAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Specialty)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
    }
}

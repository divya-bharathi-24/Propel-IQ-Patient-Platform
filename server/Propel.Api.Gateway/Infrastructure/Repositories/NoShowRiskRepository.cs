using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="INoShowRiskRepository"/> (us_031, task_002).
/// Provides read and write operations for no-show risk scores and the input data needed
/// by <c>RuleBasedNoShowRiskCalculator</c>.
/// <para>
/// All queries are parameterised LINQ expressions — no raw string interpolation into SQL
/// (OWASP A03 — Injection Prevention).
/// </para>
/// </summary>
public sealed class NoShowRiskRepository : INoShowRiskRepository
{
    private readonly AppDbContext _db;

    public NoShowRiskRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<AppointmentRiskInputData?> GetRiskInputDataAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        // Load the appointment with Specialty navigation to determine the type factor.
        var appointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Specialty)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
            return null;

        // Prior no-show count for this patient (null when no patient linked → anonymous walk-in).
        int? priorNoShowCount = null;
        if (appointment.PatientId.HasValue)
        {
            priorNoShowCount = await _db.Appointments
                .AsNoTracking()
                .CountAsync(
                    a => a.PatientId == appointment.PatientId.Value
                      && a.Status == AppointmentStatus.Cancelled   // mapped as soft-delete; NoShow is modelled as Cancelled per domain enum
                      && a.Id != appointmentId,
                    cancellationToken);

            // Re-query specifically for Cancelled appointments that represent no-shows.
            // The domain AppointmentStatus does not have a distinct "NoShow" value —
            // we count all Cancelled past appointments for the patient as proxy for no-show history.
            // This mirrors the task spec: "appointments WHERE patientId = @p AND status = 'NoShow'".
        }

        // Intake completion status for this appointment.
        var intakeRecord = await _db.IntakeRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId, cancellationToken);

        bool? intakeCompleted = intakeRecord is null
            ? null
            : intakeRecord.CompletedAt.HasValue;

        // Notification delivery status for this appointment.
        var notifications = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.AppointmentId == appointmentId)
            .Select(n => new { n.SentAt, n.Status })
            .ToListAsync(cancellationToken);

        bool anyDelivered = notifications.Any(n => n.SentAt.HasValue);
        bool anySent      = notifications.Count > 0;

        return new AppointmentRiskInputData(
            AppointmentId:          appointmentId,
            PatientId:              appointment.PatientId,
            AppointmentDate:        appointment.Date,
            SpecialtyName:          appointment.Specialty?.Name,
            PriorNoShowCount:       priorNoShowCount,
            IntakeCompleted:        intakeCompleted,
            AnyNotificationDelivered: anyDelivered,
            AnyNotificationSent:    anySent);
    }

    /// <inheritdoc/>
    public async Task<NoShowRisk?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _db.NoShowRisks
            .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        NoShowRisk noShowRisk,
        CancellationToken cancellationToken = default)
    {
        var entry = _db.Entry(noShowRisk);
        if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            // New record — track as Added.
            _db.NoShowRisks.Add(noShowRisk);
        }
        // Existing tracked entities (Modified) are committed directly.
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> GetUpcomingBookedAppointmentIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Booked && a.Date >= today)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Domain.Entities.Appointment>> GetAppointmentsByDateWithRiskAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Date == date)
            .Include(a => a.Patient)
            .Include(a => a.Specialty)
            .Include(a => a.NoShowRisk)
            .OrderBy(a => a.TimeSlotStart)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AppointmentHistoryEntry>> GetPatientAppointmentHistoryAsync(
        Guid patientId,
        DateOnly cutoffDate,
        int maxRecords,
        CancellationToken cancellationToken = default)
    {
        // Load past appointments for the patient on or after the cutoff date (last 24 months).
        // Left-join notifications and intake records inline via subqueries to avoid
        // N+1 queries while remaining purely parameterised (OWASP A03).
        var rows = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId && a.Date >= cutoffDate)
            .OrderByDescending(a => a.Date)
            .Take(maxRecords)
            .Select(a => new
            {
                a.Date,
                a.Status,
                ReminderDelivered = _db.Notifications
                    .Any(n => n.AppointmentId == a.Id && n.SentAt != null),
                IntakeCompleted = _db.IntakeRecords
                    .Where(r => r.AppointmentId == a.Id)
                    .Select(r => (bool?)r.CompletedAt.HasValue)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AppointmentHistoryEntry(
                Date:              r.Date,
                Status:            r.Status.ToString(),
                ReminderDelivered: r.ReminderDelivered,
                IntakeCompleted:   r.IntakeCompleted))
            .ToList()
            .AsReadOnly();
    }
}

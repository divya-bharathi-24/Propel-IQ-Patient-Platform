using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAppointmentReminderRepository"/> (US_033, task_001).
/// Provides read-only appointment queries for the <c>ReminderSchedulerService</c>.
/// <para>
/// The <c>AppointmentConfiguration</c> global query filter excludes <see cref="AppointmentStatus.Cancelled"/>
/// appointments. This repository uses <c>IgnoreQueryFilters()</c> to bypass that filter so
/// the scheduler can detect Cancelled appointments and trigger suppression (AC-4).
/// </para>
/// All queries use <c>AsNoTracking()</c> — read-only (AD-2).
/// No raw SQL interpolation — all queries use parameterised LINQ (OWASP A03).
/// </summary>
public sealed class AppointmentReminderRepository : IAppointmentReminderRepository
{
    private readonly AppDbContext _db;

    public AppointmentReminderRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Appointment>> GetAppointmentsForReminderEvaluationAsync(
        int[] intervalHours,
        int tickWindowMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        if (intervalHours.Length == 0)
            return [];

        var utcNow         = DateTime.UtcNow;
        var maxIntervalHrs = intervalHours.Max();

        // Upper bound: the furthest-out reminder window (e.g. 48 h from now).
        // Lower bound: today (appointments that have already passed are irrelevant).
        // We add the tick window to capture appointments near the upper boundary.
        var upperBoundDate = DateOnly.FromDateTime(utcNow.AddHours(maxIntervalHrs).AddMinutes(tickWindowMinutes));
        var lowerBoundDate = DateOnly.FromDateTime(utcNow);

        // IgnoreQueryFilters() bypasses the Cancelled soft-delete filter so we can also
        // detect Cancelled appointments that need suppression (AC-4).
        var candidates = await _db.Appointments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Cancelled)
                     && a.Date >= lowerBoundDate
                     && a.Date <= upperBoundDate
                     && a.PatientId != null)        // exclude anonymous walk-ins — no patient to notify
            .ToListAsync(cancellationToken);

        // Apply precise interval-window filter in-memory.
        // This keeps the DB query simple (date range) while ensuring we only process
        // appointments that fall within any of the configured reminder windows.
        var tickWindow = TimeSpan.FromMinutes(tickWindowMinutes);

        return candidates
            .Where(a => AppointmentStartUtc(a) is DateTime start
                     && intervalHours.Any(h =>
                     {
                         var windowCenter = start.AddHours(-h);
                         return windowCenter >= utcNow - tickWindow
                             && windowCenter <= utcNow + tickWindow;
                     }))
            .ToList();
    }

    /// <summary>
    /// Combines <see cref="Appointment.Date"/> and <see cref="Appointment.TimeSlotStart"/>
    /// into a UTC <see cref="DateTime"/>. Returns <c>null</c> if <c>TimeSlotStart</c> is absent
    /// (e.g., all-day or unscheduled appointments — excluded from reminder evaluation).
    /// </summary>
    private static DateTime? AppointmentStartUtc(Appointment a)
    {
        if (a.TimeSlotStart is null)
            return null;

        return new DateTime(
            a.Date.Year, a.Date.Month, a.Date.Day,
            a.TimeSlotStart.Value.Hour, a.TimeSlotStart.Value.Minute, 0,
            DateTimeKind.Utc);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Appointment>> GetBookedFutureAppointmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var utcNow    = DateTime.UtcNow;
        var todayDate = DateOnly.FromDateTime(utcNow);

        // Query all Booked appointments from today onwards with a patient (not anonymous walk-ins).
        // IgnoreQueryFilters() is NOT used here — we only want Booked appointments (no Cancelled).
        // All queries use AsNoTracking() — read-only (AD-2).
        // No raw SQL interpolation — parameterised LINQ (OWASP A03).
        var candidates = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Status    == AppointmentStatus.Booked
                     && a.Date      >= todayDate
                     && a.PatientId != null
                     && a.TimeSlotStart != null)
            .ToListAsync(cancellationToken);

        // Filter in-memory for appointments whose computed UTC start is strictly in the future.
        return candidates
            .Where(a => AppointmentStartUtc(a) is DateTime start && start > utcNow)
            .ToList();
    }
}

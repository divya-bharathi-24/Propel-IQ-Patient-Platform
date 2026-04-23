using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Read-only repository abstraction for appointment queries used by the reminder scheduler (US_033, task_001)
/// and the settings recalculation handler (US_033, AC-3).
/// Separated from <see cref="IAppointmentBookingRepository"/> to follow ISP: the scheduler
/// only needs to read appointment data for reminder evaluation, not perform booking writes.
/// All queries are parameterised LINQ expressions — no raw SQL interpolation (OWASP A03).
/// </summary>
public interface IAppointmentReminderRepository
{
    /// <summary>
    /// Returns all <see cref="Appointment"/> records that are relevant for reminder evaluation:
    /// appointments with <see cref="AppointmentStatus.Booked"/> or
    /// <see cref="AppointmentStatus.Cancelled"/> status where the appointment time falls within
    /// any of the configured <paramref name="intervalHours"/> windows from now.
    /// <para>
    /// The query uses <c>AsNoTracking()</c> — read-only (AD-2).
    /// Booked appointments are candidates for job creation; Cancelled appointments trigger suppression.
    /// </para>
    /// </summary>
    /// <param name="intervalHours">
    /// Array of reminder interval offsets in hours (e.g., [48, 24, 2]).
    /// The query returns appointments where <c>appointment_start</c> falls within
    /// [now + intervalHours[i] - tickWindow, now + intervalHours[i] + tickWindow] for any i.
    /// </param>
    /// <param name="tickWindowMinutes">
    /// Half-width of the evaluation window around each interval mark (default: 5 minutes,
    /// matching the scheduler poll interval). Prevents missing appointments at exact tick boundaries.
    /// </param>
    Task<IReadOnlyList<Appointment>> GetAppointmentsForReminderEvaluationAsync(
        int[] intervalHours,
        int tickWindowMinutes = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="Appointment"/> records with <see cref="AppointmentStatus.Booked"/>
    /// status and a computed UTC start time in the future (US_033, AC-3).
    /// Used by <c>UpdateReminderIntervalsCommandHandler</c> to create new <c>Notification</c>
    /// records when additional reminder intervals are configured.
    /// Anonymous walk-in appointments (where <c>PatientId</c> is null) are excluded.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetBookedFutureAppointmentsAsync(
        CancellationToken cancellationToken = default);
}

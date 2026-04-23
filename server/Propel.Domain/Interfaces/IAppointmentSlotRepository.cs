namespace Propel.Domain.Interfaces;

/// <summary>
/// Lightweight projection of a booked appointment time slot (US_018, task_002).
/// Returned by <see cref="IAppointmentSlotRepository"/> to compute slot grid availability
/// without materialising full <c>Appointment</c> entities.
/// </summary>
public sealed record BookedSlotReadModel(TimeOnly TimeSlotStart, TimeOnly TimeSlotEnd);

/// <summary>
/// Repository abstraction for querying booked appointment slots (US_018, task_002).
/// Implementations live in the infrastructure layer (Propel.Api.Gateway) and use
/// EF Core projections with <c>AsNoTracking()</c>.
/// <para>
/// <c>specialtyId</c> and <c>date</c> are always derived from validated query parameters —
/// never from unvalidated user input (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public interface IAppointmentSlotRepository
{
    /// <summary>
    /// Returns all booked or arrived appointment slots for the given specialty and date.
    /// Cancelled and Completed appointments are excluded so they do not block re-booking.
    /// </summary>
    Task<IReadOnlyList<BookedSlotReadModel>> GetBookedSlotsAsync(
        Guid specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

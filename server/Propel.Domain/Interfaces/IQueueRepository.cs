using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for same-day queue read and write operations (US_027, task_002).
/// Implementations live in the infrastructure layer (Propel.Api.Gateway) and use EF Core.
/// <para>
/// All queries are parameterised — no raw string interpolation into SQL (OWASP A03).
/// </para>
/// </summary>
public interface IQueueRepository
{
    /// <summary>
    /// Returns all <see cref="Appointment"/> records for today (UTC) including their
    /// related <see cref="QueueEntry"/> and <see cref="Patient"/> navigation properties,
    /// ordered by <see cref="Appointment.TimeSlotStart"/> ASC.
    /// Uses <c>AsNoTracking()</c> — read-only projection (AD-2).
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetTodayAppointmentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an <see cref="Appointment"/> by its primary key with its
    /// <see cref="QueueEntry"/> eagerly included for write operations.
    /// Returns <c>null</c> when no match is found.
    /// </summary>
    Task<Appointment?> GetAppointmentWithQueueEntryAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all pending EF Core change tracker mutations in a single atomic call.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

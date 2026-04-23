using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for no-show risk read and write operations (us_031, task_002).
/// Implementations live in the infrastructure layer (Propel.Api.Gateway) and use EF Core.
/// <para>
/// All queries are parameterised — no raw string interpolation into SQL (OWASP A03).
/// </para>
/// </summary>
public interface INoShowRiskRepository
{
    /// <summary>
    /// Resolves all data required by <c>INoShowRiskCalculator</c> to score an appointment
    /// (prior no-shows, intake status, notification delivery, specialty name, lead time).
    /// Returns <c>null</c> when the appointment does not exist.
    /// </summary>
    Task<AppointmentRiskInputData?> GetRiskInputDataAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing <see cref="NoShowRisk"/> record for the given appointment,
    /// or <c>null</c> if none has been calculated yet (used for UPSERT logic).
    /// </summary>
    Task<NoShowRisk?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new <see cref="NoShowRisk"/> record or updates an existing one
    /// (score, severity, factors, calculatedAt) within a single <c>SaveChangesAsync</c> call.
    /// </summary>
    Task UpsertAsync(
        NoShowRisk noShowRisk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the IDs of all upcoming booked appointments (status = Booked, date >= today UTC)
    /// queried for the hourly batch recalculation job (us_031, AC-4).
    /// Uses <c>AsNoTracking()</c> — read-only (AD-2).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUpcomingBookedAppointmentIdsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns appointments for a given calendar date with their associated
    /// <see cref="NoShowRisk"/> (left join — risk is nullable) and patient name
    /// for <c>GET /api/staff/appointments</c> (us_031, AC-1).
    /// Ordered by <see cref="Appointment.TimeSlotStart"/> ASC.
    /// Uses <c>AsNoTracking()</c> — read-only.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetAppointmentsByDateWithRiskAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns historical appointment entries for a patient on or after <paramref name="cutoffDate"/>,
    /// ordered descending by date and capped at <paramref name="maxRecords"/> (AIR-O01 budget compliance).
    /// Used by <c>SemanticKernelNoShowRiskAugmenter</c> to build the behavioral context payload (us_031, task_003).
    /// Each entry includes notification delivery and intake completion status.
    /// Uses <c>AsNoTracking()</c> — read-only.
    /// </summary>
    Task<IReadOnlyList<AppointmentHistoryEntry>> GetPatientAppointmentHistoryAsync(
        Guid patientId,
        DateOnly cutoffDate,
        int maxRecords,
        CancellationToken cancellationToken = default);
}

namespace Propel.Domain.Interfaces;

// ── Dashboard read models (US_016, TASK_002) ─────────────────────────────────
// Lightweight projection carriers used by IPatientDashboardRepository.
// These are not domain entities — they represent a pre-aggregated read model
// optimised for a single dashboard page load (NFR-001: 2-second p95 target).

/// <summary>
/// Projection of an upcoming appointment with the pending-intake flag (US_016, AC-1, AC-2).
/// Produced by a server-side EF Core SELECT projection — no full entity materialisation.
/// </summary>
public sealed record UpcomingAppointmentReadModel(
    Guid Id,
    DateOnly Date,
    TimeOnly? TimeSlotStart,
    string SpecialtyName,
    string Status,
    bool HasPendingIntake);

/// <summary>
/// Projection of a clinical document upload record (US_016, AC-3).
/// Includes all <c>ProcessingStatus</c> values including <c>Failed</c> so the client
/// can present a retry option (task edge case).
/// </summary>
public sealed record DocumentReadModel(
    Guid Id,
    string FileName,
    DateTime UploadedAt,
    string ProcessingStatus);

/// <summary>
/// Aggregated dashboard read model returned by <see cref="IPatientDashboardRepository"/>.
/// Combines upcoming appointments, document history, and the 360° view-verified flag.
/// </summary>
public sealed record PatientDashboardReadModel(
    IReadOnlyList<UpcomingAppointmentReadModel> UpcomingAppointments,
    IReadOnlyList<DocumentReadModel> Documents,
    bool ViewVerified);

/// <summary>
/// Repository abstraction for the patient dashboard aggregation query (US_016, TASK_002).
/// Implementations live in the infrastructure layer (Propel.Api.Gateway) and use
/// EF Core projections with <c>AsNoTracking()</c> for optimal read performance (NFR-001).
/// <para>
/// <c>patientId</c> is always extracted from the JWT claim — never from request parameters
/// (OWASP A01 — Broken Access Control). The controller is responsible for this extraction.
/// </para>
/// </summary>
public interface IPatientDashboardRepository
{
    /// <summary>
    /// Returns the aggregated dashboard data for the given patient.
    /// Upcoming appointments exclude <c>Completed</c> and <c>Cancelled</c> statuses and
    /// are scoped to today and future dates.
    /// </summary>
    Task<PatientDashboardReadModel> GetDashboardAsync(
        Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> when the patient has at least one <c>Notification</c> record
    /// with <c>status = Failed</c> AND <c>retryCount &gt;= 2</c>, indicating that both the
    /// initial delivery attempt and the single automated retry failed (US_021, AC-4).
    /// Surfaced as a dismissable inline banner on the patient dashboard (US_016).
    /// </summary>
    Task<bool> HasEmailDeliveryFailureAsync(
        Guid patientId,
        CancellationToken ct = default);
}

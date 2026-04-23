using System.Text.Json;
using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="IntakeRecord"/> data access (US_017, task_002).
/// All methods are patient-scoped to prevent cross-patient data leakage (OWASP A01).
/// </summary>
public interface IIntakeRepository
{
    /// <summary>
    /// Returns the full <see cref="IntakeRecord"/> for the given appointment, scoped to
    /// <paramref name="patientId"/> so a patient can never access another patient's intake (OWASP A01).
    /// Returns <c>null</c> when no record exists.
    /// </summary>
    Task<IntakeRecord?> GetByAppointmentIdAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the <c>draftData</c> JSONB column and <c>lastModifiedAt</c> on an existing
    /// <see cref="IntakeRecord"/>. Does NOT touch <c>completedAt</c>, <c>source</c>, or
    /// any of the four main JSONB columns (AC-3, AC-4).
    /// Creates a new draft-only record if none exists for the (appointmentId, patientId) pair.
    /// </summary>
    Task UpsertDraftAsync(
        Guid appointmentId,
        Guid patientId,
        JsonDocument draftData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the <see cref="IntakeRecord"/>: UPDATE if the record is already tracked by
    /// EF Core (i.e., was loaded via <see cref="GetByAppointmentIdAsync"/>), INSERT otherwise.
    /// Never creates a second row for the same (patientId, appointmentId) pair (DR-004, FR-010).
    /// </summary>
    Task<IntakeRecord> UpsertAsync(
        IntakeRecord record,
        CancellationToken cancellationToken = default);

    // ── US_030 — AI session resume & offline draft sync (task_002) ───────────

    /// <summary>
    /// Returns <c>true</c> when any <see cref="IntakeRecord"/> exists for the given
    /// (appointmentId, patientId) pair, regardless of source or completion status.
    /// Used as a fast ownership check before AI session resume (OWASP A01).
    /// </summary>
    Task<bool> ExistsForPatientAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    // ── US_029 — Manual intake form (task_002) ────────────────────────────────

    /// <summary>
    /// Returns the latest incomplete manual draft for the given appointment, scoped to
    /// <paramref name="patientId"/> (OWASP A01). Filters on <c>source = Manual</c> and
    /// <c>completedAt IS NULL</c>. Returns <c>null</c> when no draft exists.
    /// </summary>
    Task<IntakeRecord?> GetManualDraftAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the completed AI-sourced intake record for the given appointment, scoped to
    /// <paramref name="patientId"/> (OWASP A01). Filters on <c>source = AI</c> and
    /// <c>completedAt IS NOT NULL</c>. Returns <c>null</c> when none exists.
    /// </summary>
    Task<IntakeRecord?> GetAiExtractedAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes the given <see cref="IntakeRecord"/>. Only called for incomplete manual
    /// drafts (<c>completedAt IS NULL</c>) — enforcement is in the command handler (US_029).
    /// </summary>
    Task RemoveAsync(
        IntakeRecord record,
        CancellationToken cancellationToken = default);
}

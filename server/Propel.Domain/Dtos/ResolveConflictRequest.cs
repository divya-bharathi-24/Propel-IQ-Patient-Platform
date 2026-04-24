namespace Propel.Domain.Dtos;

/// <summary>
/// Request body for <c>POST /api/conflicts/{id}/resolve</c> (EP-008-II/us_044, task_003, AC-3).
/// <para>
/// <see cref="ResolvedValue"/> is the authoritative value chosen by Staff from the two conflicting
/// values (or a custom entry). It is persisted on the <c>DataConflict</c> record.
/// </para>
/// <para>
/// <see cref="ResolutionNote"/> is an optional free-text justification recorded in the
/// <c>AuditLog.Details</c> payload only — it is not stored on the entity (FR-058).
/// </para>
/// </summary>
public sealed record ResolveConflictRequest(
    /// <summary>
    /// The authoritative resolved value for the conflicting field.
    /// Required; max 1,000 characters (validated by <c>ResolveConflictCommandValidator</c>).
    /// </summary>
    string ResolvedValue,

    /// <summary>
    /// Optional free-text justification for the resolution decision.
    /// Recorded in the immutable AuditLog entry only (FR-057, FR-058); max 2,000 characters.
    /// </summary>
    string? ResolutionNote);

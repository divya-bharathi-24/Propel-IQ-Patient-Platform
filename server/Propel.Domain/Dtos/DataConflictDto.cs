namespace Propel.Domain.Dtos;

/// <summary>
/// API-safe DTO for a <c>DataConflict</c> record returned to Staff via
/// <c>GET /api/patients/{patientId}/conflicts</c> and
/// <c>POST /api/conflicts/{id}/resolve</c> (EP-008-II/us_044, task_003, AC-3, AC-4).
/// <para>
/// Source document internal GUIDs are mapped to human-readable <see cref="SourceDocument1Name"/>
/// and <see cref="SourceDocument2Name"/> strings so that internal storage identifiers are never
/// exposed to the frontend (OWASP A01).
/// </para>
/// <para>
/// <see cref="Severity"/> and <see cref="ResolutionStatus"/> are serialised as strings for
/// readability in Swagger and client-side switch statements (TR-006).
/// </para>
/// </summary>
public sealed record DataConflictDto(
    /// <summary>Primary key of the DataConflict record.</summary>
    Guid Id,

    /// <summary>Patient whose clinical data this conflict relates to.</summary>
    Guid PatientId,

    /// <summary>The clinical field where the two source documents disagree (e.g. "BloodType").</summary>
    string FieldName,

    /// <summary>The field value extracted from the first source document.</summary>
    string Value1,

    /// <summary>Human-readable name of the first source document (maps to ClinicalDocument.FileName).</summary>
    string SourceDocument1Name,

    /// <summary>The field value extracted from the second source document.</summary>
    string Value2,

    /// <summary>Human-readable name of the second source document (maps to ClinicalDocument.FileName).</summary>
    string SourceDocument2Name,

    /// <summary>Clinical significance of the conflict: Low, Medium, High, or Critical.</summary>
    string Severity,

    /// <summary>Current resolution state: Unresolved, Resolved, or PendingReview.</summary>
    string ResolutionStatus,

    /// <summary>The value chosen by Staff as the authoritative resolution. Null when unresolved.</summary>
    string? ResolvedValue,

    /// <summary>ID of the Staff member who resolved the conflict. Null when unresolved.</summary>
    Guid? ResolvedBy,

    /// <summary>UTC timestamp when the conflict was resolved. Null when unresolved.</summary>
    DateTimeOffset? ResolvedAt);

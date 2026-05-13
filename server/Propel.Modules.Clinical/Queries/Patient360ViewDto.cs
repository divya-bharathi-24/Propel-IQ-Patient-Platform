namespace Propel.Modules.Clinical.Queries;

/// <summary>
/// Source citation for a single extracted clinical data item (AC-2).
/// </summary>
public sealed record SourceCitationDto(
    string DocumentName,
    int? PageNumber,
    DateTime UploadedAt);

/// <summary>
/// A single aggregated clinical data item within a section (AC-1, AC-2).
/// </summary>
public sealed record ClinicalItemDto(
    string FieldName,
    string Value,
    decimal Confidence,

    /// <summary>True when <see cref="Confidence"/> is below 0.80 (AIR-003).</summary>
    bool IsLowConfidence,

    IReadOnlyList<SourceCitationDto> Sources);

/// <summary>
/// A grouping of clinical items by <c>dataType</c> (e.g. Vitals, Medications).
/// Serialized as <c>sectionType</c> to match the Angular frontend DTO (task_001).
/// </summary>
public sealed record ClinicalSectionDto(
    string SectionType,
    IReadOnlyList<ClinicalItemDto> Items);

/// <summary>
/// Status entry for a single clinical document associated with the patient.
/// </summary>
public sealed record DocumentStatusDto(
    Guid DocumentId,
    string DocumentName,
    string Status,
    DateTime UploadedAt);

/// <summary>
/// Summary of a single unresolved conflict — used in the frontend conflict gate (AC-4).
/// </summary>
public sealed record ConflictSummaryDto(string FieldName, string Reason);

/// <summary>
/// Full conflict object surfaced in the 360-view response (US_044, task_002).
/// Matches the Angular <c>DataConflictDto</c> interface in <c>patient-360-view.service.ts</c>.
/// </summary>
public sealed record DataConflictItemDto(
    Guid ConflictId,
    string FieldName,
    string Severity,
    string ResolutionStatus,
    string Value1,
    string SourceDoc1,
    string Value2,
    string SourceDoc2,
    string? ResolvedValue);

/// <summary>
/// Top-level response DTO for <c>GET /api/staff/patients/{patientId}/360-view</c> (AC-1, AC-2).
/// Shape matches the Angular <c>Patient360ViewDto</c> interface (task_001, US_041).
/// </summary>
public sealed record Patient360ViewDto(
    Guid PatientId,

    /// <summary>'Unverified' or 'Verified' — flattened from PatientProfileVerification.</summary>
    string VerificationStatus,

    /// <summary>UTC timestamp of last verification; null when Unverified.</summary>
    DateTime? VerifiedAt,

    /// <summary>Display name of the Staff member who verified; null when Unverified.</summary>
    string? VerifiedByStaffName,

    /// <summary>Summary of all unresolved Critical conflicts (AC-4 gate).</summary>
    IReadOnlyList<ConflictSummaryDto> UnresolvedCriticalConflicts,

    /// <summary>Full conflict objects for the conflict-resolution UI (US_044).</summary>
    IReadOnlyList<DataConflictItemDto> Conflicts,

    IReadOnlyList<DocumentStatusDto> Documents,

    IReadOnlyList<ClinicalSectionDto> Sections);

/// <summary>
/// Response DTO for <c>POST /api/staff/patients/{patientId}/360-view/verify</c> (AC-3).
/// Matches the Angular <c>VerifyProfileResponseDto</c> interface.
/// </summary>
public sealed record VerifyProfileResponseDto(
    string VerificationStatus,
    DateTime VerifiedAt,
    string VerifiedByStaffName);

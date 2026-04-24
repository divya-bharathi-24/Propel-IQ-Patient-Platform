namespace Propel.Modules.Clinical.Queries;

/// <summary>
/// Source citation for a single extracted clinical data item (AC-2).
/// </summary>
public sealed record SourceCitationDto(
    string DocumentName,
    int PageNumber,
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
/// </summary>
public sealed record ClinicalSectionDto(
    string SectionName,
    IReadOnlyList<ClinicalItemDto> Items);

/// <summary>
/// Status entry for a single clinical document associated with the patient.
/// </summary>
public sealed record DocumentStatusDto(
    Guid DocumentId,
    string FileName,
    string Status,
    DateTime UploadedAt);

/// <summary>
/// Verification metadata enriched onto the 360-view response when a
/// <c>PatientProfileVerification</c> record exists for the patient.
/// </summary>
public sealed record VerificationInfoDto(
    string Status,
    DateTime? VerifiedAt,
    string? VerifiedByName);

/// <summary>
/// Top-level response DTO for <c>GET /api/staff/patients/{patientId}/360-view</c> (AC-1, AC-2).
/// </summary>
public sealed record Patient360ViewDto(
    Guid PatientId,
    IReadOnlyList<ClinicalSectionDto> Sections,
    IReadOnlyList<DocumentStatusDto> Documents,

    /// <summary>True when the number of completed documents exceeds 10 (SLA gate).</summary>
    bool ExceedsSlaThreshold,

    /// <summary>Populated when a verification record exists; null otherwise.</summary>
    VerificationInfoDto? Verification);

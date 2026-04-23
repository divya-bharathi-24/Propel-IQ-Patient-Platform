namespace Propel.Api.Gateway.Features.Documents.Dtos;

/// <summary>
/// A single entry in the patient document history returned by
/// <c>GET /api/staff/patients/{patientId}/documents</c> (US_039, AC-2).
/// </summary>
/// <param name="Id">Document primary key.</param>
/// <param name="FileName">Original file name at upload time.</param>
/// <param name="FileSize">File size in bytes.</param>
/// <param name="SourceType">
///   <c>"PatientUpload"</c> or <c>"StaffUpload"</c> — drives the AC-2 badge display.
/// </param>
/// <param name="UploadedByName">
///   Staff display name for <c>StaffUpload</c> documents; <c>null</c> for patient self-uploads.
/// </param>
/// <param name="EncounterReference">Optional appointment reference string; <c>null</c> if not provided at upload.</param>
/// <param name="ProcessingStatus">AI extraction pipeline lifecycle state (Pending / Processing / Completed / Failed).</param>
/// <param name="UploadedAt">UTC timestamp when the document was uploaded.</param>
/// <param name="IsDeletable">
///   <c>true</c> when the document is a <c>StaffUpload</c>, was uploaded within the last 24 hours,
///   and has not yet been soft-deleted (US_039 edge case — wrong patient uploaded).
/// </param>
public record DocumentHistoryItemDto(
    Guid Id,
    string FileName,
    long FileSize,
    string SourceType,
    string? UploadedByName,
    string? EncounterReference,
    string ProcessingStatus,
    DateTime UploadedAt,
    bool IsDeletable
);

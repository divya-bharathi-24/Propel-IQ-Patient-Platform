namespace Propel.Api.Gateway.Features.Documents.Dtos;

/// <summary>
/// Per-file result within a patient batch upload response (US_038, AC-4).
/// Enables partial batch recovery: a failed file reports <see cref="Success"/> = <c>false</c>
/// and an <see cref="ErrorMessage"/> while successfully uploaded files in the same batch
/// are already persisted and are NOT rolled back (AC-4 edge case).
/// </summary>
/// <param name="FileName">Original file name as submitted by the client.</param>
/// <param name="Success"><c>true</c> when the file was validated, encrypted, stored, and persisted; <c>false</c> on any failure.</param>
/// <param name="ErrorMessage">Human-readable failure reason when <see cref="Success"/> is <c>false</c>; <c>null</c> on success.</param>
/// <param name="DocumentId">The newly created <see cref="Propel.Domain.Entities.ClinicalDocument"/> ID; <c>null</c> on failure.</param>
public record UploadFileResultDto(
    string FileName,
    bool Success,
    string? ErrorMessage,
    Guid? DocumentId = null
);

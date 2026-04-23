namespace Propel.Api.Gateway.Features.Documents.Dtos;

/// <summary>
/// Response payload for <c>POST /api/staff/documents/upload</c> (US_039, AC-1).
/// Returns the created document ID plus an optional encounter warning when the provided
/// <c>encounterReference</c> did not match any existing appointment (US_039 edge case).
/// </summary>
/// <param name="Id">The newly created <see cref="Propel.Domain.Entities.ClinicalDocument"/> ID.</param>
/// <param name="EncounterWarning">
///   <c>true</c> when <c>encounterReference</c> was provided but no matching appointment was found.
///   The document is still persisted with a 201 response — no 4xx is raised (US_039 edge case).
/// </param>
/// <param name="WarningMessage">Human-readable warning message when <see cref="EncounterWarning"/> is <c>true</c>; null otherwise.</param>
public record UploadNoteResponseDto(
    Guid Id,
    bool EncounterWarning,
    string? WarningMessage
);

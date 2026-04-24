namespace Propel.Domain.Dtos;

/// <summary>
/// API response envelope returned by <c>GET /api/patients/{patientId}/medical-codes</c>
/// (EP-008-II/us_042, task_002, AC-1, AC-2, AC-3, AC-4).
/// <para>
/// When <see cref="Suggestions"/> is empty the <see cref="Message"/> field carries the reason
/// so the Staff UI (US_043) can surface it instead of an empty code list (edge case EC-1).
/// </para>
/// </summary>
public sealed class MedicalCodeSuggestionsResponse
{
    /// <summary>
    /// Merged ICD-10 and CPT suggestions sorted by descending confidence.
    /// Empty when no clinical data is available or the model returned no valid codes (EC-1).
    /// Codes with <see cref="MedicalCodeSuggestionDto.Confidence"/> &lt; 0.80 are flagged via
    /// <see cref="MedicalCodeSuggestionDto.LowConfidence"/> = <c>true</c> (AC-4).
    /// </summary>
    public IReadOnlyList<MedicalCodeSuggestionDto> Suggestions { get; init; } = [];

    /// <summary>
    /// Human-readable status message surfaced to the caller when <see cref="Suggestions"/> is
    /// empty or a non-fatal condition was encountered by the pipeline.
    /// <c>null</c> when suggestions are present and no special condition applies.
    /// </summary>
    public string? Message { get; init; }
}

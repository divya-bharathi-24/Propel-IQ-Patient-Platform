namespace Propel.Domain.Dtos;

/// <summary>
/// Wrapper result returned by <c>IMedicalCodingOrchestrator.SuggestCodesAsync</c>
/// (EP-008-II/us_042, task_001, AC-1, AC-2, AC-4).
/// <para>
/// When <see cref="Suggestions"/> is empty and <see cref="Message"/> is non-null, the caller
/// should surface the message to the clinician instead of an empty code list
/// (edge case: no clinical documents available — AC-1 / EC-1).
/// </para>
/// </summary>
public sealed class MedicalCodingSuggestionResult
{
    /// <summary>
    /// Merged ICD-10 and CPT suggestions sorted by descending confidence.
    /// Empty when no clinical data is available or the model returned no valid codes.
    /// </summary>
    public IReadOnlyList<MedicalCodeSuggestionDto> Suggestions { get; init; } = [];

    /// <summary>
    /// Human-readable status message surfaced to the caller when the suggestion list is empty
    /// or the pipeline encountered a non-fatal condition.
    /// </summary>
    public string? Message { get; init; }
}

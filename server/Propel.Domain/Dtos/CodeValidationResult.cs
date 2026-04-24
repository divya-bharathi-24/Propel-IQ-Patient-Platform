namespace Propel.Domain.Dtos;

/// <summary>
/// Response returned by <c>POST /api/medical-codes/validate</c> (EP-008-II/us_043, task_002, AC-4).
/// <para>
/// When <see cref="Valid"/> is <c>true</c>, <see cref="NormalizedCode"/> contains the canonical
/// form of the code (uppercase, standard formatting) ready for use in the confirm payload.
/// When <see cref="Valid"/> is <c>false</c>, <see cref="Message"/> contains a human-readable
/// explanation that the frontend can surface to the Staff user.
/// </para>
/// </summary>
public sealed record CodeValidationResult(
    /// <summary><c>true</c> when the code exists in the ICD-10-CM or CPT reference library.</summary>
    bool Valid,

    /// <summary>Identifies the code system that was validated against ("ICD10" or "CPT").</summary>
    string CodeType,

    /// <summary>
    /// Canonical uppercase form of the code, e.g. "J18.9" or "99213".
    /// <c>null</c> when <see cref="Valid"/> is <c>false</c>.
    /// </summary>
    string? NormalizedCode,

    /// <summary>
    /// Human-readable rejection reason. <c>null</c> when <see cref="Valid"/> is <c>true</c>.
    /// </summary>
    string? Message);

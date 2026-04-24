using Propel.Domain.Enums;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Static rule-based classifier that maps clinical field types to a conflict severity level
/// (EP-008-II/us_044, task_001, AC-1, AIR-S02).
/// <para>
/// Severity rules (deterministic — no AI involvement):
/// <list type="bullet">
///   <item><description>
///     <see cref="DataConflictSeverity.Critical"/>: fields in the critical clinical set
///     (medications, allergies, diagnoses). Critical conflicts block patient profile
///     verification (AC-4).
///   </description></item>
///   <item><description>
///     <see cref="DataConflictSeverity.Warning"/>: all other field types (vitals,
///     ancillary demographics). Surfaced for review but do not block verification.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public static class ConflictSeverityClassifier
{
    /// <summary>
    /// Field name prefixes (case-insensitive) that map to <see cref="DataConflictSeverity.Critical"/>.
    /// Prefix matching allows sub-types (e.g. "MedicationDosage") to inherit the parent
    /// "Medication" classification without enumerating every variant.
    /// </summary>
    private static readonly HashSet<string> CriticalFieldPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Medication",
        "Allergy",
        "Diagnosis",
        "DiagnosisDate"
    };

    /// <summary>
    /// Classifies a conflict by field name into <see cref="DataConflictSeverity.Critical"/>
    /// or <see cref="DataConflictSeverity.Warning"/> (AC-1, FR-054).
    /// </summary>
    /// <param name="fieldName">
    /// The clinical field name from <see cref="Domain.Entities.ExtractedData.FieldName"/>.
    /// Case-insensitive matching is applied.
    /// </param>
    /// <returns>
    /// <see cref="DataConflictSeverity.Critical"/> when <paramref name="fieldName"/> starts with
    /// any critical prefix; <see cref="DataConflictSeverity.Warning"/> otherwise.
    /// </returns>
    public static DataConflictSeverity Classify(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return DataConflictSeverity.Warning;

        foreach (var prefix in CriticalFieldPrefixes)
        {
            if (fieldName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return DataConflictSeverity.Critical;
        }

        return DataConflictSeverity.Warning;
    }
}

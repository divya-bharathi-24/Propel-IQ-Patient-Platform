using System.Text.Json;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Validates GPT-4o extraction output against the <c>ClinicalExtractionSchema</c> and
/// applies content filtering for harmful clinical recommendations (US_040, AIR-Q03, AIR-S04).
/// <para>
/// Two enforcement layers:
/// <list type="bullet">
///   <item><description>
///     Schema completeness (AIR-Q03): all six category keys must be present and contain only
///     well-formed <see cref="ClinicalExtractionField"/> entries (non-null <c>fieldName</c> and
///     <c>value</c>; confidence in [0, 1]).  Violations throw <see cref="ExtractionSchemaValidationException"/>.
///   </description></item>
///   <item><description>
///     Content safety (AIR-S04): field values are checked for a deny-list of terms that indicate
///     hallucinated prescriptions, dosage escalations, or unsolicited diagnostic recommendations.
///     Violations throw <see cref="ExtractionSchemaValidationException"/> so the caller sets
///     <c>ProcessingStatus = Failed</c> and surfaces the document for manual review.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ExtractionGuardrailFilter
{
    /// <summary>
    /// Terms that indicate the model has generated clinical recommendations rather than
    /// extracting data from the source text (AIR-S04 harmful content signal list).
    /// Case-insensitive substring match is applied per field value.
    /// </summary>
    private static readonly string[] HarmfulContentPatterns =
    [
        "you should take",
        "i recommend",
        "i suggest",
        "increase dosage",
        "decrease dosage",
        "stop taking",
        "discontinue",
        "consult a doctor",
        "seek emergency",
        "call 911",
        "this is not medical advice",
        "as an ai"
    ];

    /// <summary>
    /// Validates <paramref name="output"/> for schema completeness (AIR-Q03) and
    /// content safety (AIR-S04).
    /// </summary>
    /// <param name="output">Deserialized GPT-4o response. Must not be null.</param>
    /// <exception cref="ExtractionSchemaValidationException">
    /// Thrown when any field has an invalid schema structure or contains harmful content.
    /// </exception>
    public void Validate(ClinicalExtractionOutput output)
    {
        ValidateCategory(output.Vitals,         "vitals");
        ValidateCategory(output.Medications,    "medications");
        ValidateCategory(output.Diagnoses,      "diagnoses");
        ValidateCategory(output.Allergies,      "allergies");
        ValidateCategory(output.Immunizations,  "immunizations");
        ValidateCategory(output.SurgicalHistory,"surgicalHistory");
    }

    private static void ValidateCategory(List<ClinicalExtractionField> fields, string categoryName)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];

            // AIR-Q03: schema completeness — fieldName and value must be non-empty strings.
            if (string.IsNullOrWhiteSpace(field.FieldName))
                throw new ExtractionSchemaValidationException(
                    $"Schema violation in '{categoryName}[{i}]': fieldName is null or empty.");

            if (string.IsNullOrWhiteSpace(field.Value))
                throw new ExtractionSchemaValidationException(
                    $"Schema violation in '{categoryName}[{i}]': value is null or empty.");

            // AIR-Q03: confidence must be in [0, 1].
            if (field.Confidence < 0m || field.Confidence > 1m)
                throw new ExtractionSchemaValidationException(
                    $"Schema violation in '{categoryName}[{i}]': confidence {field.Confidence} is outside [0, 1].");

            // AIR-S04: content safety — scan for harmful recommendation patterns.
            CheckContentSafety(field.Value, categoryName, i);
            if (field.SourceTextSnippet is not null)
                CheckContentSafety(field.SourceTextSnippet, categoryName, i);
        }
    }

    private static void CheckContentSafety(string content, string categoryName, int index)
    {
        foreach (var pattern in HarmfulContentPatterns)
        {
            if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                throw new ExtractionSchemaValidationException(
                    $"Content safety violation in '{categoryName}[{index}]': " +
                    $"field value contains a disallowed pattern indicating AI-generated recommendation " +
                    $"rather than extracted clinical data (AIR-S04). Pattern: '{pattern}'.");
        }
    }
}

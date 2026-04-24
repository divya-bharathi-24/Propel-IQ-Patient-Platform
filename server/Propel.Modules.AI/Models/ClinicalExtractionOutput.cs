using System.Text.Json.Serialization;

namespace Propel.Modules.AI.Models;

/// <summary>
/// Deserialization target for the GPT-4o JSON response from the
/// <c>ClinicalDataExtraction</c> prompt template (US_040, AC-3, AIR-Q03).
/// <para>
/// Each category maps to an <see cref="ExtractedDataType"/> value:
/// <c>Vitals</c>, <c>Medications</c>, <c>Diagnoses</c>, <c>Allergies</c>,
/// <c>Immunizations</c>, and <c>SurgicalHistory</c> (mapped to <c>History</c>).
/// </para>
/// All arrays default to empty to guarantee non-null iteration when GPT-4o
/// omits a category (schema guard, AIR-Q03).
/// </summary>
public sealed class ClinicalExtractionOutput
{
    [JsonPropertyName("vitals")]
    public List<ClinicalExtractionField> Vitals { get; set; } = [];

    [JsonPropertyName("medications")]
    public List<ClinicalExtractionField> Medications { get; set; } = [];

    [JsonPropertyName("diagnoses")]
    public List<ClinicalExtractionField> Diagnoses { get; set; } = [];

    [JsonPropertyName("allergies")]
    public List<ClinicalExtractionField> Allergies { get; set; } = [];

    [JsonPropertyName("immunizations")]
    public List<ClinicalExtractionField> Immunizations { get; set; } = [];

    [JsonPropertyName("surgicalHistory")]
    public List<ClinicalExtractionField> SurgicalHistory { get; set; } = [];
}

/// <summary>
/// A single extracted clinical field returned by GPT-4o (US_040, AC-3, AIR-001, AIR-002).
/// <see cref="Confidence"/> must be in [0, 1]; fields with <c>Confidence &lt; 0.80</c>
/// are flagged for priority staff review (AIR-003).
/// </summary>
public sealed class ClinicalExtractionField
{
    [JsonPropertyName("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>AI confidence score in range [0, 1] (AIR-001). Values below 0.80 trigger priority review (AIR-003).</summary>
    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    [JsonPropertyName("sourcePageNumber")]
    public int SourcePageNumber { get; set; }

    /// <summary>Verbatim text snippet from the source excerpt that supports this field value (AIR-002).</summary>
    [JsonPropertyName("sourceTextSnippet")]
    public string? SourceTextSnippet { get; set; }
}

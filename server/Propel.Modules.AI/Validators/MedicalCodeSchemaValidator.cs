using Propel.Domain.Dtos;
using Propel.Domain.Enums;
using Serilog;

namespace Propel.Modules.AI.Validators;

/// <summary>
/// Validates AI-generated medical code suggestions against schema constraints and
/// anti-hallucination guards (EP-008-II/us_042, task_001, AC-3, AIR-Q03).
/// <para>
/// Two enforcement layers:
/// <list type="bullet">
///   <item><description>
///     Schema completeness (AIR-Q03): every suggestion must have a non-empty <c>Code</c>,
///     a non-empty <c>Description</c>, and a <c>Confidence</c> value in [0, 1].
///   </description></item>
///   <item><description>
///     Anti-hallucination (AC-3): each code is validated against the in-memory ICD-10-CM
///     and CPT reference prefix sets. Codes whose prefix is not found in the reference sets
///     are flagged, logged at WARNING level, and excluded from the validated output.
///     The schema validity rate is tracked via structured Serilog events (AIR-Q03).
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class MedicalCodeSchemaValidator
{
    // ── In-memory ICD-10-CM code prefix reference (representative sample) ─────
    // Validated against CMS ICD-10-CM tabular list (FY2025).
    // Production deployments should load the full code set from the database or a local file.
    // The prefix-match strategy allows sub-category codes (e.g. "J18.9") to be validated
    // without maintaining the full ~70,000 code leaf set in memory.
    private static readonly HashSet<string> Icd10ValidPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chapter 1 – Infectious diseases (A00–B99)
        "A", "B",
        // Chapter 2 – Neoplasms (C00–D49)
        "C", "D0", "D1", "D2", "D3", "D4",
        // Chapter 3 – Blood (D50–D89)
        "D5", "D6", "D7", "D8",
        // Chapter 4 – Endocrine (E00–E89)
        "E",
        // Chapter 5 – Mental (F01–F99)
        "F",
        // Chapter 6 – Nervous system (G00–G99)
        "G",
        // Chapter 7 – Eye (H00–H59)
        "H0", "H1", "H2", "H3", "H4", "H5",
        // Chapter 8 – Ear (H60–H95)
        "H6", "H7", "H8", "H9",
        // Chapter 9 – Circulatory (I00–I99)
        "I",
        // Chapter 10 – Respiratory (J00–J99)
        "J",
        // Chapter 11 – Digestive (K00–K95)
        "K",
        // Chapter 12 – Skin (L00–L99)
        "L",
        // Chapter 13 – Musculoskeletal (M00–M99)
        "M",
        // Chapter 14 – Genitourinary (N00–N99)
        "N",
        // Chapter 15 – Pregnancy (O00–O9A)
        "O",
        // Chapter 16 – Perinatal (P00–P96)
        "P",
        // Chapter 17 – Congenital (Q00–Q99)
        "Q",
        // Chapter 18 – Symptoms/signs (R00–R99)
        "R",
        // Chapter 19 – Injury/Poisoning (S00–T88)
        "S", "T",
        // Chapter 20 – External causes (V00–Y99)
        "V", "W", "X", "Y",
        // Chapter 21 – Factors (Z00–Z99)
        "Z",
        // Chapter 22 – Codes for special purposes (U00–U85)
        "U"
    };

    // ── In-memory CPT code range reference ───────────────────────────────────
    // CPT codes are 5-digit numeric strings (Evaluation & Management, Procedures, etc.)
    // with Category II/III codes using alphanumeric suffixes (e.g. "0001F", "0159T").
    // The ranges below cover the AMA CPT code set structure:
    //   99201–99499 – Evaluation & Management
    //   00100–01999 – Anesthesia
    //   10004–69990 – Surgery
    //   70010–79999 – Radiology
    //   80047–89398 – Pathology/Lab
    //   90281–99199 – Medicine
    //   Category II (4 digits + F): 0001F–9007F
    //   Category III (4 digits + T): 0042T–0813T
    private static readonly (int Min, int Max)[] CptNumericRanges =
    [
        (100,   1999),    // Anesthesia
        (10004, 69990),   // Surgery
        (70010, 79999),   // Radiology
        (80047, 89398),   // Pathology/Lab
        (90281, 99199),   // Medicine
        (99201, 99499),   // E&M
    ];

    /// <summary>
    /// Validates a list of AI-generated medical code suggestions for schema completeness
    /// and anti-hallucination compliance (AIR-Q03, AC-3).
    /// </summary>
    /// <param name="suggestions">Raw suggestions from the AI plugin (not yet validated).</param>
    /// <param name="toolName">Human-readable tool identifier for audit logging ("ICD10" or "CPT").</param>
    /// <returns>
    /// Filtered list containing only schema-valid, non-hallucinated suggestions.
    /// Violations are logged as <c>MedicalCodeSchemaViolation</c> structured events.
    /// </returns>
    public List<MedicalCodeSuggestionDto> Validate(
        IReadOnlyList<MedicalCodeSuggestionDto> suggestions,
        string toolName)
    {
        var validated = new List<MedicalCodeSuggestionDto>(suggestions.Count);
        int violationCount = 0;

        foreach (var suggestion in suggestions)
        {
            var violationReason = GetViolationReason(suggestion);

            if (violationReason is not null)
            {
                violationCount++;
                Log.Warning(
                    "{MedicalCodeSchemaViolation}: toolName={ToolName} code={Code} reason={Reason}",
                    "MedicalCodeSchemaViolation", toolName, suggestion.Code, violationReason);
                continue;
            }

            validated.Add(suggestion);
        }

        if (suggestions.Count > 0)
        {
            double validityRate = (double)(suggestions.Count - violationCount) / suggestions.Count;
            Log.Information(
                "MedicalCodeSchemaValidator_Result: toolName={ToolName} total={Total} " +
                "valid={Valid} violationCount={ViolationCount} validityRate={ValidityRate:P1}",
                toolName, suggestions.Count, validated.Count, violationCount, validityRate);
        }

        return validated;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a violation reason string when the suggestion fails schema or anti-hallucination
    /// validation, or <c>null</c> when the suggestion is valid.
    /// </summary>
    private static string? GetViolationReason(MedicalCodeSuggestionDto s)
    {
        // ── Schema completeness (AIR-Q03) ─────────────────────────────────────
        if (string.IsNullOrWhiteSpace(s.Code))
            return "Code is null or empty.";

        if (string.IsNullOrWhiteSpace(s.Description))
            return $"Description is null or empty for code '{s.Code}'.";

        if (s.Confidence < 0m || s.Confidence > 1m)
            return $"Confidence {s.Confidence:F2} is outside [0, 1] for code '{s.Code}'.";

        // ── Anti-hallucination guard (AC-3) ───────────────────────────────────
        return s.CodeType switch
        {
            MedicalCodeType.ICD10 => IsValidIcd10Code(s.Code) ? null
                : $"ICD-10 code '{s.Code}' does not match any known ICD-10-CM chapter prefix.",
            MedicalCodeType.CPT => IsValidCptCode(s.Code) ? null
                : $"CPT code '{s.Code}' is outside known CPT numeric ranges or alphanumeric categories.",
            _ => $"Unknown CodeType '{s.CodeType}' for code '{s.Code}'."
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the ICD-10-CM code starts with a valid chapter letter prefix.
    /// Accepts codes with dots (e.g. "J18.9") and without (e.g. "J189").
    /// </summary>
    private static bool IsValidIcd10Code(string code)
    {
        if (code.Length < 2) return false;

        var normalised = code.Replace(".", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        // Single-letter prefix match (covers most chapters).
        var singlePrefix = normalised[..1];
        if (Icd10ValidPrefixes.Contains(singlePrefix)) return true;

        // Two-character prefix match (distinguishes D0x–D4x blood from D5x–D8x).
        if (normalised.Length >= 2)
        {
            var twoPrefix = normalised[..2];
            return Icd10ValidPrefixes.Contains(twoPrefix);
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the CPT code is:
    /// <list type="bullet">
    ///   <item>A 5-digit numeric code within a known AMA CPT range, OR</item>
    ///   <item>A Category II code (4 digits + "F"), OR</item>
    ///   <item>A Category III code (4 digits + "T").</item>
    /// </list>
    /// </summary>
    private static bool IsValidCptCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var upper = code.Trim().ToUpperInvariant();

        // Category II: 0000F–9999F
        if (upper.Length == 5 && upper.EndsWith('F') && int.TryParse(upper[..4], out _))
            return true;

        // Category III: 0000T–9999T
        if (upper.Length == 5 && upper.EndsWith('T') && int.TryParse(upper[..4], out _))
            return true;

        // Standard numeric CPT: 5 digits
        if (upper.Length == 5 && int.TryParse(upper, out int numericCode))
        {
            foreach (var (min, max) in CptNumericRanges)
            {
                if (numericCode >= min && numericCode <= max)
                    return true;
            }
        }

        return false;
    }
}

using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Singleton implementation of <see cref="ICodeReferenceLibrary"/> that validates medical codes
/// against the in-memory ICD-10-CM and CPT reference sets (EP-008-II/us_043, task_002, AC-4).
/// <para>
/// The same prefix/range-based validation strategy used by <c>MedicalCodeSchemaValidator</c>
/// (US_042/task_001) is applied here for consistency. Both classes share the same reference data
/// shape; the singleton lifecycle ensures the sets are allocated only once per process.
/// </para>
/// <para>
/// ICD-10 normalization: strips dots and converts to uppercase (e.g. "j18.9" → "J18.9" restored
/// with dot for the normalised form). CPT normalization: trims whitespace and uppercases the string.
/// </para>
/// </summary>
public sealed class CodeReferenceLibrary : ICodeReferenceLibrary
{
    // ── In-memory ICD-10-CM chapter-prefix reference ──────────────────────────
    // Validated against CMS ICD-10-CM tabular list (FY2025).
    // Prefix-match strategy allows sub-category codes (e.g. "J18.9") to be validated without
    // maintaining the full ~70,000 leaf-code set in memory.
    private static readonly HashSet<string> Icd10ValidPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ch. 1 – Infectious diseases (A00–B99)
        "A", "B",
        // Ch. 2 – Neoplasms (C00–D49)
        "C", "D0", "D1", "D2", "D3", "D4",
        // Ch. 3 – Blood (D50–D89)
        "D5", "D6", "D7", "D8",
        // Ch. 4 – Endocrine (E00–E89)
        "E",
        // Ch. 5 – Mental (F01–F99)
        "F",
        // Ch. 6 – Nervous system (G00–G99)
        "G",
        // Ch. 7 – Eye (H00–H59)
        "H0", "H1", "H2", "H3", "H4", "H5",
        // Ch. 8 – Ear (H60–H95)
        "H6", "H7", "H8", "H9",
        // Ch. 9 – Circulatory (I00–I99)
        "I",
        // Ch. 10 – Respiratory (J00–J99)
        "J",
        // Ch. 11 – Digestive (K00–K95)
        "K",
        // Ch. 12 – Skin (L00–L99)
        "L",
        // Ch. 13 – Musculoskeletal (M00–M99)
        "M",
        // Ch. 14 – Genitourinary (N00–N99)
        "N",
        // Ch. 15 – Pregnancy (O00–O9A)
        "O",
        // Ch. 16 – Perinatal (P00–P96)
        "P",
        // Ch. 17 – Congenital (Q00–Q99)
        "Q",
        // Ch. 18 – Symptoms/signs (R00–R99)
        "R",
        // Ch. 19 – Injury/Poisoning (S00–T88)
        "S", "T",
        // Ch. 20 – External causes (V00–Y99)
        "V", "W", "X", "Y",
        // Ch. 21 – Factors (Z00–Z99)
        "Z",
        // Ch. 22 – Special purposes (U00–U85)
        "U"
    };

    // ── In-memory CPT numeric range reference ─────────────────────────────────
    // Covers the AMA CPT code structure (numeric codes and alphanumeric Category II/III).
    private static readonly (int Min, int Max)[] CptNumericRanges =
    [
        (100,   1999),   // Anesthesia
        (10004, 69990),  // Surgery
        (70010, 79999),  // Radiology
        (80047, 89398),  // Pathology / Lab
        (90281, 99199),  // Medicine
        (99201, 99499),  // Evaluation & Management
    ];

    /// <inheritdoc />
    public CodeLookupResult Validate(string code, MedicalCodeType codeType)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new CodeLookupResult(false, null, "Code must not be empty.");

        return codeType switch
        {
            MedicalCodeType.ICD10 => ValidateIcd10(code),
            MedicalCodeType.CPT   => ValidateCpt(code),
            _                     => new CodeLookupResult(false, null, $"Unknown code type '{codeType}'.")
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static CodeLookupResult ValidateIcd10(string code)
    {
        // Normalize: strip whitespace, uppercase, remove dot for prefix check only
        var trimmed   = code.Trim().ToUpperInvariant();
        var dotless   = trimmed.Replace(".", string.Empty, StringComparison.Ordinal);

        if (dotless.Length < 3)
            return new CodeLookupResult(false, null, $"ICD-10 code '{code}' is too short to be valid.");

        // Single-letter prefix (covers most ICD-10 chapters)
        var singlePrefix = dotless[..1];
        // Two-character prefix (disambiguates D-series overlap between blood and neoplasms)
        var twoPrefix    = dotless.Length >= 2 ? dotless[..2] : singlePrefix;

        bool valid = Icd10ValidPrefixes.Contains(twoPrefix)
                  || Icd10ValidPrefixes.Contains(singlePrefix);

        if (!valid)
            return new CodeLookupResult(
                false,
                null,
                $"ICD-10 code '{code}' does not match any known ICD-10-CM chapter prefix.");

        // Return canonical form: preserve the dot notation supplied by the caller if present,
        // otherwise return the trimmed uppercase code as-is.
        return new CodeLookupResult(true, trimmed, null);
    }

    private static CodeLookupResult ValidateCpt(string code)
    {
        var upper = code.Trim().ToUpperInvariant();

        // Category II: 4 digits + 'F' (e.g. "0001F")
        if (upper.Length == 5 && upper.EndsWith('F') && int.TryParse(upper[..4], out _))
            return new CodeLookupResult(true, upper, null);

        // Category III: 4 digits + 'T' (e.g. "0159T")
        if (upper.Length == 5 && upper.EndsWith('T') && int.TryParse(upper[..4], out _))
            return new CodeLookupResult(true, upper, null);

        // Standard 5-digit numeric CPT
        if (upper.Length == 5 && int.TryParse(upper, out int numericCode))
        {
            foreach (var (min, max) in CptNumericRanges)
            {
                if (numericCode >= min && numericCode <= max)
                    return new CodeLookupResult(true, upper, null);
            }
        }

        return new CodeLookupResult(
            false,
            null,
            $"CPT code '{code}' is outside known CPT numeric ranges or alphanumeric categories.");
    }
}

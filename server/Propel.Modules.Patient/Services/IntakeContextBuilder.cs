using System.Text;
using System.Text.Json;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Services;

/// <summary>
/// Pure string-builder helper that converts a partially-filled <see cref="IntakeFieldMap"/>
/// into a plain-English bullet-list summary for consumption by the Semantic Kernel resume prompt
/// (US_030, AC-2, checklist item 3).
/// <para>
/// This class intentionally has NO dependency on Semantic Kernel — it is a pure transformation
/// kept separate so it remains fully unit-testable without mocking the AI stack.
/// </para>
/// <para>
/// PII remains within the system during this transformation; no external data transmission
/// occurs here (AIR-S03 note in task spec — SK call is internal-only).
/// </para>
/// </summary>
public static class IntakeContextBuilder
{
    /// <summary>
    /// Approximate characters-per-token ratio used to guard the 500-token output ceiling.
    /// 4 chars ≈ 1 token is a safe heuristic for English clinical text.
    /// </summary>
    private const int CharsPerToken = 4;

    /// <summary>Maximum output tokens; task spec: ≤500 tokens.</summary>
    private const int MaxOutputTokens = 500;

    private const int MaxChars = MaxOutputTokens * CharsPerToken; // 2 000 chars

    /// <summary>
    /// Builds a bullet-list context summary from non-null, non-empty fields in
    /// <paramref name="fields"/> across the four intake sections (Demographics → Medical History
    /// → Symptoms → Medications). Null and empty sections are omitted.
    /// Output is hard-capped at <see cref="MaxChars"/> characters (≈500 tokens).
    /// </summary>
    /// <param name="fields">The partially-filled intake snapshot from the frontend.</param>
    /// <returns>
    /// Plain-English bullet-list text ready to be injected as context into the AI resume prompt.
    /// Returns an empty string when all sections are null or empty.
    /// </returns>
    public static string BuildContextSummary(IntakeFieldMap fields)
    {
        var sb = new StringBuilder();

        AppendSection(sb, "Demographics", fields.Demographics);
        AppendSection(sb, "Medical History", fields.MedicalHistory);
        AppendSection(sb, "Symptoms", fields.Symptoms);
        AppendSection(sb, "Medications", fields.Medications);

        var summary = sb.ToString().TrimEnd();

        // Hard cap at MaxChars to respect the ≤500-token budget (task spec).
        return summary.Length <= MaxChars ? summary : summary[..MaxChars];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void AppendSection(StringBuilder sb, string header, JsonDocument? doc)
    {
        if (doc is null) return;

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        var lines = BuildBulletLines(root);
        if (lines.Count == 0) return;

        // Stop appending if we have already reached the character ceiling.
        if (sb.Length >= MaxChars) return;

        sb.AppendLine($"{header}:");

        foreach (var line in lines)
        {
            if (sb.Length + line.Length + Environment.NewLine.Length > MaxChars)
                break;

            sb.AppendLine(line);
        }
    }

    private static List<string> BuildBulletLines(JsonElement root)
    {
        var lines = new List<string>();

        foreach (var prop in root.EnumerateObject())
        {
            var val = prop.Value;

            if (val.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            var displayValue = val.ValueKind == JsonValueKind.String
                ? val.GetString() ?? string.Empty
                : val.GetRawText();

            if (string.IsNullOrWhiteSpace(displayValue))
                continue;

            // Format the property name from camelCase → readable label (e.g. "dateOfBirth" → "Date Of Birth")
            var label = ToReadableLabel(prop.Name);
            lines.Add($"  - {label}: {displayValue}");
        }

        return lines;
    }

    /// <summary>
    /// Converts a camelCase property name to a title-cased readable label.
    /// Example: "dateOfBirth" → "Date Of Birth".
    /// </summary>
    private static string ToReadableLabel(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase))
            return camelCase;

        var result = new StringBuilder();
        result.Append(char.ToUpperInvariant(camelCase[0]));

        for (int i = 1; i < camelCase.Length; i++)
        {
            if (char.IsUpper(camelCase[i]))
            {
                result.Append(' ');
                result.Append(camelCase[i]);
            }
            else
            {
                result.Append(camelCase[i]);
            }
        }

        return result.ToString();
    }
}

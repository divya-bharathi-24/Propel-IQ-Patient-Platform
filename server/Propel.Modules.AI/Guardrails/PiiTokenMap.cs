using System.Text.RegularExpressions;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Static compiled regex dictionary mapping PII patterns to anonymized tokens (AIR-S01, task_001).
/// <para>
/// Six PII categories are replaced in sequence:
/// <list type="bullet">
///   <item><description><b>Email</b> — replaced first to prevent partial matches by downstream patterns.</description></item>
///   <item><description><b>Phone</b> — North American E.164-compatible formats including punctuation variants.</description></item>
///   <item><description><b>DOB</b> — MM/DD/YYYY, MM-DD-YYYY, M/D/YY, M-D-YY patterns.</description></item>
///   <item><description><b>Insurance ID</b> — 2–3 uppercase letters followed by 6–12 digits (common US payer format).</description></item>
///   <item><description><b>Address</b> — street number followed by a street name keyword.</description></item>
///   <item><description><b>Name</b> — capitalised first + last name pairs (applied last to minimise false positives).</description></item>
/// </list>
/// Patterns use <see cref="RegexOptions.Compiled"/> for performance in the hot-path prompt filter.
/// </para>
/// </summary>
internal static class PiiTokenMap
{
    /// <summary>
    /// Ordered list of (compiled pattern, replacement token) pairs.
    /// Applied in declaration order via <see cref="Regex.Replace"/> — order matters.
    /// </summary>
    internal static readonly IReadOnlyList<(Regex Pattern, string Token)> Entries =
    [
        // Email — RFC 5321 local-part @ domain (most specific; applied first)
        (
            new Regex(
                @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
                RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "[EMAIL]"
        ),

        // Phone — +1 (123) 456-7890, 123-456-7890, (123) 456 7890, 1234567890, etc.
        (
            new Regex(
                @"\b(\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]?\d{3}[\s.\-]?\d{4}\b",
                RegexOptions.Compiled),
            "[PHONE]"
        ),

        // Date of birth — MM/DD/YYYY, MM-DD-YYYY, M/D/YY, M-D-YY
        (
            new Regex(
                @"\b\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}\b",
                RegexOptions.Compiled),
            "[DOB]"
        ),

        // Insurance ID — 2–3 uppercase letters + 6–12 digits (US payer format, e.g. BC1234567)
        (
            new Regex(
                @"\b[A-Z]{2,3}\d{6,12}\b",
                RegexOptions.Compiled),
            "[INSURANCE_ID]"
        ),

        // Street address — leading number followed by a street name keyword
        (
            new Regex(
                @"\b\d{1,5}\s+[A-Za-z0-9\s]{2,30}(?:Street|St|Avenue|Ave|Boulevard|Blvd|Road|Rd|Lane|Ln|Drive|Dr|Court|Ct|Place|Pl|Way|Circle|Cir)\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "[ADDRESS]"
        ),

        // Full name — Title-case first + last name pair (applied last; most permissive)
        (
            new Regex(
                @"\b[A-Z][a-z]{1,20}\s[A-Z][a-z]{1,20}\b",
                RegexOptions.Compiled),
            "[NAME]"
        ),
    ];
}

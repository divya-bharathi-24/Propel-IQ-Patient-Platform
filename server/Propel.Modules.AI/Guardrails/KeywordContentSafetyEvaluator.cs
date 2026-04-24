using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Phase 1 implementation of <see cref="IContentSafetyEvaluator"/> using a configurable
/// keyword blocklist loaded from <c>AiSafety:BlockedKeywords</c> in <c>appsettings.json</c>
/// (AIR-S04, task_001, AC-3).
/// <para>
/// Each keyword in the blocklist is compiled into a case-insensitive <see cref="Regex"/> word-boundary
/// pattern. The response text is checked against each pattern in order; the first match returns
/// <c>IsBlocked = true</c> with the matched keyword as <c>BlockedReason</c>.
/// </para>
/// <para>
/// Replace or augment this implementation with Azure AI Content Safety in Phase 2 without
/// changing call sites — only <see cref="IContentSafetyEvaluator"/> needs to be re-registered in DI.
/// </para>
/// </summary>
public sealed class KeywordContentSafetyEvaluator : IContentSafetyEvaluator
{
    private readonly IReadOnlyList<(string Keyword, Regex Pattern)> _blocklist;

    public KeywordContentSafetyEvaluator(IConfiguration configuration)
    {
        var keywords = configuration
            .GetSection("AiSafety:BlockedKeywords")
            .Get<List<string>>() ?? [];

        _blocklist = keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => (
                k,
                new Regex(
                    $@"\b{Regex.Escape(k)}\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase)))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public Task<ContentSafetyResult> EvaluateAsync(string responseText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return Task.FromResult(new ContentSafetyResult(false, null));

        foreach (var (keyword, pattern) in _blocklist)
        {
            if (pattern.IsMatch(responseText))
                return Task.FromResult(new ContentSafetyResult(true, keyword));
        }

        return Task.FromResult(new ContentSafetyResult(false, null));
    }
}

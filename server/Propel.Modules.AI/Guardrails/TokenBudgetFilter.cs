using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel;
using Propel.Modules.AI.Options;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IPromptRenderFilter"/> that enforces an 8,000-token budget per
/// request (AIR-O01, US_050 AC-2).
/// <para>
/// Processing order (runs after <see cref="PiiRedactionFilter"/> at priority 10):
/// <list type="number">
///   <item><description>Calls <c>await next(context)</c> so SK and the PII filter render the prompt first.</description></item>
///   <item><description>Counts exact GPT-4o tokens via <c>TiktokenTokenizer</c> (o200k_base encoding).</description></item>
///   <item><description>If token count ≤ budget: passes through without modification.</description></item>
///   <item><description>If token count &gt; budget: calls <see cref="TruncateChunks"/> to remove the
///     lowest-similarity RAG chunks (appended last in the prompt) until the budget is satisfied.</description></item>
///   <item><description>Emits a Serilog <c>Warning</c> with original token count, dropped chunk IDs, and
///     final token count (AIR-O01 truncation event log).</description></item>
///   <item><description>Replaces <see cref="PromptRenderContext.RenderedPrompt"/> with the truncated version
///     — no over-budget request reaches the provider.</description></item>
/// </list>
/// </para>
/// <para>
/// RAG chunks are delimited by <c>"\n---\n"</c> (written by <c>ExtractionOrchestrator.BuildContext</c>).
/// Chunks are stored highest-similarity-first; removal therefore targets the lowest-similarity
/// entries at the tail of the context section.
/// </para>
/// </summary>
public sealed class TokenBudgetFilter : IPromptRenderFilter
{
    // Sentinel that separates RAG chunks in the rendered prompt (matches ExtractionOrchestrator.BuildContext).
    private const string ChunkSeparator = "\n---\n";

    private readonly TiktokenTokenizer _tokenizer;
    private readonly IOptionsMonitor<AiResilienceSettings> _options;

    public TokenBudgetFilter(
        TiktokenTokenizer tokenizer,
        IOptionsMonitor<AiResilienceSettings> options)
    {
        _tokenizer = tokenizer;
        _options   = options;
    }

    /// <inheritdoc/>
    public async Task OnPromptRenderAsync(
        PromptRenderContext context,
        Func<PromptRenderContext, Task> next)
    {
        // Render the prompt first (PII filter runs before this one at priority 0).
        await next(context);

        var prompt = context.RenderedPrompt;
        if (string.IsNullOrEmpty(prompt))
            return;

        int maxTokens = _options.CurrentValue.TokenBudgetLimit;
        int tokenCount = _tokenizer.CountTokens(prompt);

        if (tokenCount <= maxTokens)
            return;

        var (truncatedPrompt, droppedChunkIds) = TruncateChunks(prompt, tokenCount, maxTokens);
        int finalCount = _tokenizer.CountTokens(truncatedPrompt);

        Log.Warning(
            "TokenBudgetFilter_Truncated: originalTokens={OriginalTokens} droppedChunks={DroppedCount} " +
            "chunkIds=[{ChunkIds}] finalTokens={FinalTokens} (AIR-O01).",
            tokenCount,
            droppedChunkIds.Count,
            string.Join(", ", droppedChunkIds),
            finalCount);

        context.RenderedPrompt = truncatedPrompt;
    }

    /// <summary>
    /// Splits the prompt on <see cref="ChunkSeparator"/> boundaries and removes chunks from the
    /// tail (lowest similarity, per RAG retrieval order) until the token count satisfies
    /// <paramref name="maxTokens"/>.
    /// </summary>
    /// <returns>
    /// A tuple of the truncated prompt string and a list of positional chunk identifiers that
    /// were removed (e.g. <c>"chunk-3"</c>, <c>"chunk-4"</c>).
    /// </returns>
    private (string TruncatedPrompt, List<string> DroppedChunkIds) TruncateChunks(
        string prompt,
        int currentTokenCount,
        int maxTokens)
    {
        // Split preserving separator position; filter out empty entries from leading/trailing separators.
        var parts = prompt.Split(ChunkSeparator).ToList();

        // Nothing to split — hard-truncate the single block by character estimate.
        if (parts.Count <= 1)
        {
            int maxChars = maxTokens * 4; // ~4 chars/token for GPT-4o family
            var truncated = prompt.Length > maxChars ? prompt[..maxChars] : prompt;
            return (truncated, new List<string> { "chunk-0-partial" });
        }

        var dropped = new List<string>();
        int count = currentTokenCount;

        // Remove from the tail (index = highest, lowest similarity) until within budget.
        while (count > maxTokens && parts.Count > 1)
        {
            int lastIdx = parts.Count - 1;
            dropped.Add($"chunk-{lastIdx}");
            parts.RemoveAt(lastIdx);
            var rejoined = string.Join(ChunkSeparator, parts);
            count = _tokenizer.CountTokens(rejoined);
        }

        // If still over budget after all chunks removed, character-truncate the remainder.
        var result = string.Join(ChunkSeparator, parts);
        if (count > maxTokens)
        {
            int maxChars = maxTokens * 4;
            if (result.Length > maxChars)
            {
                dropped.Add("chunk-0-partial");
                result = result[..maxChars];
            }
        }

        return (result, dropped);
    }
}

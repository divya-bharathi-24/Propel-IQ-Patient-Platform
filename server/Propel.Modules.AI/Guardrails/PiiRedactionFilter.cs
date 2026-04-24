using Microsoft.SemanticKernel;
using Propel.Modules.AI.Exceptions;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IPromptRenderFilter"/> that redacts patient PII from the rendered
/// prompt string before it is transmitted to the external OpenAI provider (AIR-S01, task_001, AC-1).
/// <para>
/// Processing order:
/// <list type="number">
///   <item><description>Retrieves the rendered prompt from <see cref="PromptRenderContext.RenderedPrompt"/>.</description></item>
///   <item><description>Applies all <see cref="PiiTokenMap"/> patterns in sequence via <c>Regex.Replace</c>.</description></item>
///   <item><description>Stores the redacted prompt in <c>context.Arguments["__renderedPrompt"]</c> so that
///     <see cref="AiPromptAuditHook"/> can capture it for the audit record.</description></item>
///   <item><description>On any regex failure: logs a Serilog <c>Error</c> and throws <see cref="PiiRedactionException"/>,
///     aborting the pipeline before any data reaches OpenAI.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class PiiRedactionFilter : IPromptRenderFilter
{
    /// <summary>
    /// Key used to pass the redacted prompt text to downstream filters via
    /// <see cref="KernelArguments"/> (consumed by <see cref="AiPromptAuditHook"/>).
    /// </summary>
    public const string RenderedPromptArgumentKey = "__renderedPrompt";

    /// <inheritdoc/>
    public async Task OnPromptRenderAsync(
        PromptRenderContext context,
        Func<PromptRenderContext, Task> next)
    {
        // Allow the SK pipeline to render the prompt template first.
        await next(context);

        var rawPrompt = context.RenderedPrompt;
        if (string.IsNullOrEmpty(rawPrompt))
            return;

        string redactedPrompt;
        try
        {
            redactedPrompt = ApplyRedaction(rawPrompt);
        }
        catch (Exception ex)
        {
            // Extract session ID from arguments for structured logging — never log the prompt itself.
            var sessionId = context.Arguments.TryGetValue("sessionId", out var sid) ? sid?.ToString() : "unknown";

            Log.Error(ex,
                "PiiRedactionFilter_Failed: sessionId={SessionId} — request blocked before transmission (AIR-S01).",
                sessionId);

            throw new PiiRedactionException(
                "PII redaction failed — request blocked before transmission to OpenAI.",
                ex);
        }

        // Replace the prompt with the redacted version so the LLM never sees raw PII.
        context.RenderedPrompt = redactedPrompt;

        // Pass the redacted prompt to AiPromptAuditHook via context arguments.
        context.Arguments[RenderedPromptArgumentKey] = redactedPrompt;
    }

    private static string ApplyRedaction(string prompt)
    {
        var result = prompt;
        foreach (var (pattern, token) in PiiTokenMap.Entries)
        {
            result = pattern.Replace(result, token);
        }
        return result;
    }
}

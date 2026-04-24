using Microsoft.SemanticKernel;
using Propel.Modules.AI.Exceptions;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IFunctionInvocationFilter"/> that evaluates the AI model's
/// response for harmful, biased, or clinically inappropriate content (AIR-S04, task_001, AC-3).
/// <para>
/// Registered second in the filter chain (after <see cref="PiiRedactionFilter"/>, before
/// <see cref="AiPromptAuditHook"/>). On each function invocation:
/// <list type="number">
///   <item><description>Calls <c>await next(context)</c> to obtain the AI response.</description></item>
///   <item><description>Extracts the raw response string from <c>context.Result</c>.</description></item>
///   <item><description>Delegates to <see cref="IContentSafetyEvaluator.EvaluateAsync"/>.</description></item>
///   <item><description>If blocked: stores <c>contentFilterBlocked = true</c> in context arguments
///     (consumed by <see cref="AiPromptAuditHook"/>), logs a Serilog <c>Error</c>, and throws
///     <see cref="ContentSafetyException"/> so the clinical workflow falls back to manual review.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ContentSafetyFilter : IFunctionInvocationFilter
{
    /// <summary>
    /// Key used to signal downstream filters (e.g. <see cref="AiPromptAuditHook"/>) that
    /// this invocation was blocked by the content safety filter.
    /// </summary>
    public const string ContentFilterBlockedArgumentKey = "contentFilterBlocked";

    private readonly IContentSafetyEvaluator _evaluator;

    public ContentSafetyFilter(IContentSafetyEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    /// <inheritdoc/>
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        await next(context);

        var responseText = context.Result?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(responseText))
            return;

        var safetyResult = await _evaluator.EvaluateAsync(responseText, context.CancellationToken);

        if (!safetyResult.IsBlocked)
            return;

        var sessionId = context.Arguments.TryGetValue("sessionId", out var sid) ? sid?.ToString() : "unknown";

        Log.Error(
            "ContentSafetyFilter_Blocked: sessionId={SessionId} reason={BlockedReason} — " +
            "response blocked before delivery to caller (AIR-S04).",
            sessionId,
            safetyResult.BlockedReason);

        // Signal the audit hook so it records contentFilterBlocked = true.
        context.Arguments[ContentFilterBlockedArgumentKey] = true;

        throw new ContentSafetyException(safetyResult.BlockedReason ?? "content_safety_violation");
    }
}

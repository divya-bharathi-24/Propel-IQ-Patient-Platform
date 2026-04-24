using Microsoft.SemanticKernel;
using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IFunctionInvocationFilter"/> that writes an immutable
/// <see cref="AiPromptAuditLogEntry"/> for every AI kernel function invocation (AIR-S03, task_001, AC-4).
/// <para>
/// Registered last in the filter chain so it wraps both <see cref="PiiRedactionFilter"/> and
/// <see cref="ContentSafetyFilter"/>. Runs in a try/finally pattern to ensure the audit record
/// is written even when <c>ContentSafetyFilter</c> throws <see cref="Exceptions.ContentSafetyException"/>.
/// </para>
/// <para>
/// Contract:
/// <list type="bullet">
///   <item><description>Never throws — audit write failures are caught, logged at Serilog <c>Error</c>, and swallowed
///     so they cannot disrupt the clinical workflow.</description></item>
///   <item><description>Extracts <c>__renderedPrompt</c> from context arguments (set by <see cref="PiiRedactionFilter"/>).</description></item>
///   <item><description>Extracts <c>contentFilterBlocked</c> from context arguments (set by <see cref="ContentSafetyFilter"/>).</description></item>
///   <item><description>Response text is null when the model call failed or was blocked — this is intentional.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AiPromptAuditHook : IFunctionInvocationFilter
{
    private readonly IAiPromptAuditWriter _auditWriter;

    public AiPromptAuditHook(IAiPromptAuditWriter auditWriter)
    {
        _auditWriter = auditWriter;
    }

    /// <inheritdoc/>
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        Exception? invocationException = null;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            invocationException = ex;
            throw;
        }
        finally
        {
            await WriteAuditRecordAsync(context, invocationException);
        }
    }

    private async Task WriteAuditRecordAsync(
        FunctionInvocationContext context,
        Exception? invocationException)
    {
        try
        {
            // Redacted prompt set by PiiRedactionFilter — null if that filter did not run.
            context.Arguments.TryGetValue(PiiRedactionFilter.RenderedPromptArgumentKey, out var renderedPromptObj);
            var redactedPrompt = renderedPromptObj as string;

            // contentFilterBlocked set by ContentSafetyFilter on block.
            context.Arguments.TryGetValue(ContentSafetyFilter.ContentFilterBlockedArgumentKey, out var blockedFlagObj);
            var contentFilterBlocked = blockedFlagObj is true;

            // Response text: available only when the invocation succeeded without being blocked.
            string? responseText = null;
            if (invocationException == null)
                responseText = context.Result?.GetValue<string>();

            // Requesting user and session — passed by convention via context arguments.
            context.Arguments.TryGetValue("userId", out var userIdObj);
            var userId = userIdObj?.ToString();

            context.Arguments.TryGetValue("sessionId", out var sessionIdObj);
            var sessionId = sessionIdObj?.ToString();

            var entry = new AiPromptAuditLogEntry
            {
                TimestampUtc         = DateTime.UtcNow,
                SessionId            = sessionId,
                RedactedPrompt       = redactedPrompt,
                ResponseText         = responseText,
                ModelName            = context.Function.PluginName ?? context.Function.Name,
                FunctionName         = context.Function.Name,
                RequestingUserId     = userId,
                ContentFilterBlocked = contentFilterBlocked,
                // Token counts are not available without Kernel metadata in SK 1.x;
                // task_002 persistence layer can enrich from usage metadata when available.
                PromptTokenCount     = null,
                CompletionTokenCount = null,
            };

            await _auditWriter.WriteAsync(entry);
        }
        catch (Exception ex)
        {
            // Audit write failure must never disrupt clinical workflow (AC-4 edge case).
            Log.Error(ex,
                "AiPromptAuditHook_WriteFailure: failed to persist audit record for function={FunctionName} — swallowed (AIR-S03).",
                context.Function.Name);
        }
    }
}

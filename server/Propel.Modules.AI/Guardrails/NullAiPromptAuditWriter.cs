using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Null implementation of <see cref="IAiPromptAuditWriter"/> used until the EF Core
/// persistence implementation (<c>EfAiPromptAuditWriter</c>) is provided by task_002.
/// <para>
/// Logs the audit entry at <c>Debug</c> level so that functionality is observable in
/// development without requiring the <c>AiPromptAuditLog</c> database table to exist.
/// Replace this registration in Program.cs with <c>EfAiPromptAuditWriter</c> once task_002 is complete.
/// </para>
/// </summary>
public sealed class NullAiPromptAuditWriter : IAiPromptAuditWriter
{
    /// <inheritdoc/>
    public Task WriteAsync(AiPromptAuditLogEntry entry, CancellationToken ct = default)
    {
        Log.Debug(
            "NullAiPromptAuditWriter: audit record for function={FunctionName} userId={UserId} " +
            "contentFilterBlocked={ContentFilterBlocked} timestampUtc={TimestampUtc} " +
            "— persisted in memory only (task_002 EfAiPromptAuditWriter not yet registered).",
            entry.FunctionName,
            entry.RequestingUserId,
            entry.ContentFilterBlocked,
            entry.TimestampUtc);

        return Task.CompletedTask;
    }
}

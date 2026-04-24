using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// INSERT-only EF Core implementation of <see cref="IAiPromptAuditWriter"/>
/// (EP-010/us_049, AC-4, task_002).
/// <para>
/// Contract:
/// <list type="bullet">
///   <item><description>INSERT-only — no Update or Remove calls (AD-7).</description></item>
///   <item><description>Never throws — exceptions are caught, logged at <c>Serilog.Error</c>, and swallowed
///     so that audit write failures never disrupt the clinical workflow (AC-4 edge case,
///     consistent with <c>AiPromptAuditHook</c> never-throws contract from task_001).</description></item>
///   <item><description>Replaces <c>NullAiPromptAuditWriter</c> stub registered in Program.cs during task_001.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class EfAiPromptAuditWriter : IAiPromptAuditWriter
{
    private readonly AppDbContext _context;

    public EfAiPromptAuditWriter(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(AiPromptAuditLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            var entity = new AiPromptAuditLog
            {
                Id                   = Guid.NewGuid(),
                RecordedAt           = entry.TimestampUtc,
                SessionId            = entry.SessionId,
                RequestingUserId     = entry.RequestingUserId,
                ModelName            = entry.ModelName,
                FunctionName         = entry.FunctionName,
                RedactedPrompt       = entry.RedactedPrompt,
                ResponseText         = entry.ResponseText,
                PromptTokenCount     = entry.PromptTokenCount,
                CompletionTokenCount = entry.CompletionTokenCount,
                ContentFilterBlocked = entry.ContentFilterBlocked,
            };

            await _context.AiPromptAuditLogs.AddAsync(entity, ct).ConfigureAwait(false);
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);

            Log.Debug(
                "EfAiPromptAuditWriter: audit record persisted for function={FunctionName} userId={UserId} sessionId={SessionId} (AIR-S03).",
                entry.FunctionName,
                entry.RequestingUserId,
                entry.SessionId);
        }
        catch (Exception ex)
        {
            // Swallow — audit write failure must never disrupt the clinical workflow (AC-4).
            Log.Error(ex,
                "EfAiPromptAuditWriter_WriteFailed: failed to persist AI prompt audit log for " +
                "function={FunctionName} sessionId={SessionId} userId={UserId} (AIR-S03).",
                entry.FunctionName,
                entry.SessionId,
                entry.RequestingUserId);
        }
    }
}

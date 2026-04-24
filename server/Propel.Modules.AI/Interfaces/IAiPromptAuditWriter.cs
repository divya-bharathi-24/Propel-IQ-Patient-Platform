namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Write-only interface for persisting immutable AI prompt audit log entries (AIR-S03, task_001, AC-4).
/// <para>
/// Implemented by <c>EfAiPromptAuditWriter</c> (EP-010/us_049, task_002) via EF Core INSERT-only
/// operations. The table must be append-only; no UPDATE or DELETE is ever issued against
/// <c>AiPromptAuditLog</c> rows (DR-011 — 7-year immutable retention).
/// </para>
/// <para>
/// Callers (specifically <see cref="Guardrails.AiPromptAuditHook"/>) must swallow any exception
/// from <see cref="WriteAsync"/> — audit write failure must never disrupt the clinical workflow.
/// </para>
/// </summary>
public interface IAiPromptAuditWriter
{
    /// <summary>
    /// Persists a single <see cref="AiPromptAuditLogEntry"/> as an immutable audit record.
    /// </summary>
    /// <param name="entry">The populated audit entry. Must not be null.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteAsync(AiPromptAuditLogEntry entry, CancellationToken ct = default);
}

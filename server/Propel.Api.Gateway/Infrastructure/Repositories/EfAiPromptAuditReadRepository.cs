using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Modules.AI.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// Read-only EF Core implementation of <see cref="IAiPromptAuditReadRepository"/>
/// (EP-010/us_049, AC-4, task_002).
/// <para>
/// Keyset pagination uses a composite <c>(recorded_at DESC, id DESC)</c> cursor to avoid
/// OFFSET scan degradation on large audit tables (DR-011 — 7-year retention → large row counts).
/// </para>
/// <para>
/// <c>AsNoTracking()</c> is applied to all queries — read-only, no change tracking required.
/// </para>
/// </summary>
public sealed class EfAiPromptAuditReadRepository : IAiPromptAuditReadRepository
{
    private readonly AppDbContext _db;

    public EfAiPromptAuditReadRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<List<AiPromptAuditLogReadDto>> GetPageAsync(
        string? userId,
        string? sessionId,
        (DateTime RecordedAt, Guid Id)? cursor,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = BuildBaseQuery(userId, sessionId);

        if (cursor.HasValue)
        {
            var cursorTs = cursor.Value.RecordedAt;
            var cursorId = cursor.Value.Id;
            // Keyset: rows strictly before the cursor position in descending order.
            query = query.Where(a =>
                a.RecordedAt < cursorTs ||
                (a.RecordedAt == cursorTs && a.Id.CompareTo(cursorId) < 0));
        }

        return await query
            .OrderByDescending(a => a.RecordedAt)
            .ThenByDescending(a => a.Id)
            .Take(pageSize)
            .Select(a => new AiPromptAuditLogReadDto(
                a.Id,
                a.RecordedAt,
                a.SessionId,
                a.RequestingUserId,
                a.ModelName,
                a.FunctionName,
                a.RedactedPrompt,
                a.ResponseText,
                a.PromptTokenCount,
                a.CompletionTokenCount,
                a.ContentFilterBlocked))
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(
        string? userId,
        string? sessionId,
        CancellationToken ct = default)
    {
        return await BuildBaseQuery(userId, sessionId).LongCountAsync(ct);
    }

    private IQueryable<Propel.Domain.Entities.AiPromptAuditLog> BuildBaseQuery(
        string? userId,
        string? sessionId)
    {
        IQueryable<Propel.Domain.Entities.AiPromptAuditLog> q =
            _db.AiPromptAuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(userId))
            q = q.Where(a => a.RequestingUserId == userId);

        if (!string.IsNullOrWhiteSpace(sessionId))
            q = q.Where(a => a.SessionId == sessionId);

        return q;
    }
}

using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Dtos;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Persistence.AuditLog;

/// <summary>
/// Read-only EF Core implementation of <see cref="IAuditLogReadRepository"/> (US_047, AC-1, AC-2).
/// Uses a direct <see cref="AppDbContext"/> (request-scoped) — no independent scope needed here
/// because this repository only reads; it carries no risk of write-scope contamination.
/// Dynamic <c>Where</c> predicates are composed on an <c>IQueryable&lt;Domain.Entities.AuditLog&gt;</c>
/// and translated to a single SQL statement per call.
/// Keyset pagination uses a composite <c>(timestamp DESC, id DESC)</c> cursor to avoid OFFSET
/// scan degradation on millions of rows (AC-1, NFR performance).
/// </summary>
public sealed class EfAuditLogReadRepository : IAuditLogReadRepository
{
    private readonly AppDbContext _db;

    public EfAuditLogReadRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<List<AuditLogEventDto>> GetPageAsync(
        AuditLogFilterParams filters,
        (DateTime Timestamp, Guid Id)? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BuildBaseQuery(filters);

        // Apply keyset cursor: rows strictly before the cursor position in descending order.
        // (timestamp < cursorTs) OR (timestamp == cursorTs AND id < cursorId)
        if (cursor.HasValue)
        {
            var cursorTs = cursor.Value.Timestamp;
            var cursorId = cursor.Value.Id;
            query = query.Where(a =>
                a.Timestamp < cursorTs ||
                (a.Timestamp == cursorTs && a.Id.CompareTo(cursorId) < 0));
        }

        return await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Take(pageSize)
            .Select(a => new AuditLogEventDto(
                a.Id,
                a.UserId,
                a.Role,
                a.EntityType,
                a.EntityId.ToString(),
                a.Action,
                a.IpAddress,
                a.Timestamp,
                a.Details))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(
        AuditLogFilterParams filters,
        CancellationToken cancellationToken = default)
    {
        return await BuildBaseQuery(filters).LongCountAsync(cancellationToken);
    }

    // Builds an IQueryable with dynamic filter predicates from AuditLogFilterParams.
    // No cursor or limit is applied here — those are layered on by callers.
    private IQueryable<Domain.Entities.AuditLog> BuildBaseQuery(AuditLogFilterParams filters)
    {
        IQueryable<Domain.Entities.AuditLog> q = _db.AuditLogs.AsNoTracking();

        if (filters.DateFrom.HasValue)
            q = q.Where(a => a.Timestamp >= filters.DateFrom.Value);

        if (filters.DateTo.HasValue)
            q = q.Where(a => a.Timestamp <= filters.DateTo.Value);

        if (filters.UserId.HasValue)
            q = q.Where(a => a.UserId == filters.UserId.Value);

        if (!string.IsNullOrWhiteSpace(filters.ActionType))
            q = q.Where(a => a.Action == filters.ActionType);

        if (!string.IsNullOrWhiteSpace(filters.EntityType))
            q = q.Where(a => a.EntityType == filters.EntityType);

        return q;
    }
}

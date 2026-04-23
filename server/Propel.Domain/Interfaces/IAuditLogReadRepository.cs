using Propel.Domain.Dtos;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Read-only query interface for <c>AuditLog</c> records (US_047, AD-7).
/// Intentionally exposes no write, update, or delete methods — the read surface is
/// separate from the INSERT-only <see cref="IAuditLogRepository"/> (AD-7, FR-059).
/// </summary>
public interface IAuditLogReadRepository
{
    /// <summary>
    /// Returns up to <paramref name="pageSize"/> audit events matching <paramref name="filters"/>,
    /// ordered by <c>timestamp DESC, id DESC</c>. Keyset pagination via composite cursor
    /// <c>(timestamp, id)</c> avoids OFFSET degradation on large tables (US_047, AC-1).
    /// </summary>
    Task<List<AuditLogEventDto>> GetPageAsync(
        AuditLogFilterParams filters,
        (DateTime Timestamp, Guid Id)? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of audit events matching <paramref name="filters"/>.
    /// Uses the same predicate as <see cref="GetPageAsync"/> without cursor or limit (AC-1).
    /// </summary>
    Task<long> CountAsync(
        AuditLogFilterParams filters,
        CancellationToken cancellationToken = default);
}

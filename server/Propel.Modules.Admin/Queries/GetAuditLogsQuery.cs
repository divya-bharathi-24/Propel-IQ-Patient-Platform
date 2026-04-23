using MediatR;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Queries;

/// <summary>
/// MediatR query that retrieves a paginated, filtered page of audit log events (US_047, AC-1, AC-2).
/// All filter parameters are optional. <see cref="Cursor"/> carries the opaque keyset position
/// from the previous page's <c>nextCursor</c> value.
/// </summary>
public sealed record GetAuditLogsQuery(
    DateTime? DateFrom,
    DateTime? DateTo,
    Guid? UserId,
    string? ActionType,
    string? EntityType,
    string? Cursor
) : IRequest<AuditLogPageResponse>;

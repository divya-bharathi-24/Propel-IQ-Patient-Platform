using MediatR;
using Propel.Modules.AI.Dtos;

namespace Propel.Modules.AI.Queries;

/// <summary>
/// MediatR query that returns a cursor-paginated, time-ordered (descending) page of AI prompt
/// audit records for Admin review (EP-010/us_049, AC-4, task_002).
/// All filter parameters are optional. <see cref="Cursor"/> carries the opaque keyset position
/// from the previous page's <c>nextCursor</c> value.
/// </summary>
public sealed record GetAiPromptAuditLogsQuery(
    string? UserId,
    string? SessionId,
    string? Cursor
) : IRequest<AiPromptAuditPageResponse>;

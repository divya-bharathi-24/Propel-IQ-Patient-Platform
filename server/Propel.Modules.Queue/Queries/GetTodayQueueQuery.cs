using MediatR;
using Propel.Modules.Queue;

namespace Propel.Modules.Queue.Queries;

/// <summary>
/// MediatR query for <c>GET /api/queue/today</c> (US_027, AC-1).
/// Date is resolved from <c>DateTime.UtcNow</c> inside the handler — never from the request
/// body or query string (OWASP A01 — Broken Access Control).
/// </summary>
public sealed record GetTodayQueueQuery : IRequest<IReadOnlyList<QueueItemDto>>;

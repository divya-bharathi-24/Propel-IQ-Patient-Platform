using MediatR;
using Propel.Modules.Risk.Dtos;

namespace Propel.Modules.Risk.Queries;

/// <summary>
/// MediatR query for <c>GET /api/risk/requires-attention</c> (US_032, AC-4).
/// Returns upcoming appointments with <c>NoShowRisk.score &gt; 0.66</c> AND at least one
/// Pending intervention, ordered by appointment date + time slot ascending (AD-2 CQRS read model).
/// </summary>
public sealed record GetRequiresAttentionQuery : IRequest<IReadOnlyList<RequiresAttentionItemDto>>;

using MediatR;
using Propel.Modules.AI.Dtos;

namespace Propel.Modules.AI.Queries;

/// <summary>
/// Returns a computed summary of AI quality metrics (agreementRate, hallucinationRate,
/// schemaValidityRate) over the most recent 200 events per category
/// (EP-010/us_048, AC-4, task_002).
/// Dispatched by <c>AiMetricsController</c> — Admin-only endpoint.
/// </summary>
public sealed record GetAiMetricsSummaryQuery : IRequest<AiMetricsSummaryResponse>;

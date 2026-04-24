using MediatR;
using Propel.Modules.AI.Dtos;

namespace Propel.Modules.AI.Queries;

/// <summary>
/// Returns aggregate AI operational metrics: p95 latency, avg/total token consumption,
/// error rate, circuit breaker trip count (last 24h), real-time CB open state, and
/// active model version (EP-010/us_050, AC-4, task_002).
/// <para>
/// Dispatched by <c>AiMetricsController.GetOperationalSummary</c> — Admin-only endpoint.
/// p95 latency is null with status <c>"InsufficientData"</c> when fewer than 20 latency
/// samples exist (edge-case guard, task spec).
/// </para>
/// </summary>
public sealed record GetAiOperationalMetricsSummaryQuery : IRequest<AiOperationalMetricsSummaryResponse>;

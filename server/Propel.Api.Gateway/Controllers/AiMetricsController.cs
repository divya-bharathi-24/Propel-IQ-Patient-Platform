using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Admin-only REST controller exposing AI quality and operational metric summary data
/// (EP-010/us_048, AC-4, task_002; EP-010/us_050, AC-4, task_002).
/// <para>
/// The controller-level <c>[Authorize(Roles = "Admin")]</c> attribute rejects non-Admin
/// callers with HTTP 403 before any handler logic executes (NFR-006).
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/ai-metrics")]
[Authorize(Roles = "Admin")]
public sealed class AiMetricsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiMetricsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the current computed AI quality rates (agreementRate, hallucinationRate,
    /// schemaValidityRate) and sample counts over the most recent 200 events per category
    /// (EP-010/us_048, AC-4).
    /// <para>
    /// When fewer than 50 samples exist for a given metric, the corresponding rate is returned
    /// as <c>null</c> and <c>status</c> is <c>"InsufficientData"</c> if all three are null.
    /// </para>
    /// Admin-only: HTTP 403 for non-Admin callers.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<AiMetricsSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAiMetricsSummaryQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns aggregate AI operational metrics: p95 latency, avg token consumption,
    /// error rate, circuit breaker trip count (last 24h), real-time CB open state,
    /// and active model version (EP-010/us_050, AC-4, AIR-O04).
    /// <para>
    /// <c>p95LatencyMs</c> is <c>null</c> and <c>status</c> is <c>"InsufficientData"</c> when
    /// fewer than 20 latency samples exist in the metrics window.
    /// </para>
    /// Admin-only: HTTP 403 for non-Admin callers.
    /// </summary>
    [HttpGet("operational")]
    [ProducesResponseType<AiOperationalMetricsSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperationalSummary(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAiOperationalMetricsSummaryQuery(), cancellationToken);
        return Ok(result);
    }
}

using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Queries;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="GetAiMetricsSummaryQuery"/>: computes rolling agreement rate,
/// hallucination rate, and schema validity rate from the most recent 200 events per
/// metric category (EP-010/us_048, AC-4, task_002).
/// <para>
/// Rate computation rules:
/// <list type="bullet">
///   <item><description>agreementRate = agreementCount / totalCount (window of 200 events).</description></item>
///   <item><description>hallucinationRate = hallucinatedCount / totalVerified (window of 200 samples).</description></item>
///   <item><description>schemaValidityRate = validCount / totalCount (window of 200 events).</description></item>
///   <item><description>Rate is <c>null</c> when corresponding sample count is below 50 (insufficient-data guard).</description></item>
/// </list>
/// </para>
/// <para>
/// Status determination:
/// <list type="bullet">
///   <item><description><c>"InsufficientData"</c> — all three rates are null.</description></item>
///   <item><description><c>"AgreementRateAlert"</c> — agreementRate is below 98% (AIR-Q01).</description></item>
///   <item><description><c>"HallucinationAlert"</c> — hallucinationRate exceeds 2% (AIR-Q04).</description></item>
///   <item><description><c>"OK"</c> — all computed rates are within acceptable thresholds.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class GetAiMetricsSummaryQueryHandler
    : IRequestHandler<GetAiMetricsSummaryQuery, AiMetricsSummaryResponse>
{
    private const int    WindowSize          = 200;
    private const int    MinSampleCount      = 50;
    private const double AgreementThreshold  = 0.98;
    private const double HallucinationLimit  = 0.02;

    private readonly IAiMetricsReadRepository           _metricsRepo;
    private readonly ILogger<GetAiMetricsSummaryQueryHandler> _logger;

    public GetAiMetricsSummaryQueryHandler(
        IAiMetricsReadRepository metricsRepo,
        ILogger<GetAiMetricsSummaryQueryHandler> logger)
    {
        _metricsRepo = metricsRepo;
        _logger      = logger;
    }

    public async Task<AiMetricsSummaryResponse> Handle(
        GetAiMetricsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        // Fetch all three rolling windows concurrently.
        var agreementTask       = _metricsRepo.GetRecentAgreementEventsAsync(WindowSize, cancellationToken);
        var hallucinationTask   = _metricsRepo.GetRecentVerifiedSamplesAsync(WindowSize, cancellationToken);
        var schemaValidityTask  = _metricsRepo.GetRecentSchemaValidityEventsAsync(WindowSize, cancellationToken);

        await Task.WhenAll(agreementTask, hallucinationTask, schemaValidityTask).ConfigureAwait(false);

        var (agreementTotal, agreementCount)        = await agreementTask;
        var (hallucinationTotal, hallucinatedCount)  = await hallucinationTask;
        var (schemaTotal, schemaValidCount)          = await schemaValidityTask;

        // Apply null guard: rate is null when sample count < 50.
        decimal? agreementRate      = agreementTotal      >= MinSampleCount
            ? (decimal)agreementCount    / agreementTotal
            : null;

        decimal? hallucinationRate  = hallucinationTotal  >= MinSampleCount
            ? (decimal)hallucinatedCount / hallucinationTotal
            : null;

        decimal? schemaValidityRate = schemaTotal         >= MinSampleCount
            ? (decimal)schemaValidCount  / schemaTotal
            : null;

        string status = DetermineStatus(agreementRate, hallucinationRate, schemaValidityRate);

        _logger.LogInformation(
            "GetAiMetricsSummary: agreement={Agreement} hallucination={Hallucination} schemaValidity={Schema} status={Status}",
            agreementRate, hallucinationRate, schemaValidityRate, status);

        return new AiMetricsSummaryResponse(
            AgreementRate:          agreementRate,
            HallucinationRate:      hallucinationRate,
            SchemaValidityRate:     schemaValidityRate,
            AgreementSampleCount:   agreementTotal,
            HallucinationSampleCount: hallucinationTotal,
            SchemaValiditySampleCount: schemaTotal,
            Status:                 status
        );
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string DetermineStatus(
        decimal? agreementRate,
        decimal? hallucinationRate,
        decimal? schemaValidityRate)
    {
        if (agreementRate is null && hallucinationRate is null && schemaValidityRate is null)
            return "InsufficientData";

        if (agreementRate is not null && (double)agreementRate < AgreementThreshold)
            return "AgreementRateAlert";

        if (hallucinationRate is not null && (double)hallucinationRate > HallucinationLimit)
            return "HallucinationAlert";

        return "OK";
    }
}

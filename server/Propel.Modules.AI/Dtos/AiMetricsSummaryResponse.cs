namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Response DTO for <c>GET /api/admin/ai-metrics/summary</c> (EP-010/us_048, AC-4).
/// <para>
/// Rates are null when the corresponding sample count is below the minimum threshold
/// of 50 — insufficient data guard (edge case specification).
/// </para>
/// <para>
/// Status values:
/// <list type="bullet">
///   <item><description><c>"OK"</c> — all rates are within acceptable thresholds.</description></item>
///   <item><description><c>"InsufficientData"</c> — all three rates are null (fewer than 50 samples each).</description></item>
///   <item><description><c>"AgreementRateAlert"</c> — agreement rate is below 98% (AIR-Q01).</description></item>
///   <item><description><c>"HallucinationAlert"</c> — hallucination rate exceeds 2% (AIR-Q04).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed record AiMetricsSummaryResponse(
    /// <summary>
    /// Rolling AI-Human Agreement Rate over the last 200 events.
    /// Null when fewer than 50 agreement events are available.
    /// </summary>
    decimal? AgreementRate,

    /// <summary>
    /// Rolling Hallucination Rate over the last 200 verified samples.
    /// Null when fewer than 50 hallucination events are available.
    /// </summary>
    decimal? HallucinationRate,

    /// <summary>
    /// Rolling Schema Validity Rate over the last 200 schema validation events.
    /// Null when fewer than 50 schema validity events are available.
    /// </summary>
    decimal? SchemaValidityRate,

    /// <summary>Number of agreement events in the current rolling window (max 200).</summary>
    int AgreementSampleCount,

    /// <summary>Number of verified hallucination samples in the current rolling window (max 200).</summary>
    int HallucinationSampleCount,

    /// <summary>Number of schema validity events in the current rolling window (max 200).</summary>
    int SchemaValiditySampleCount,

    /// <summary>
    /// Overall quality status derived from the computed rates.
    /// One of: <c>"OK"</c> | <c>"InsufficientData"</c> | <c>"AgreementRateAlert"</c> | <c>"HallucinationAlert"</c>.
    /// </summary>
    string Status
);

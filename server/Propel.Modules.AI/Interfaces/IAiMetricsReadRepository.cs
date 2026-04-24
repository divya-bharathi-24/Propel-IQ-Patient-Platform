namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Read-only port for querying AI quality metric history from the <c>AiQualityMetrics</c>
/// table (us_048, task_001 — concrete EF Core implementation provided by task_002).
/// <para>
/// Used by <c>AgreementRateEvaluator</c> (AIR-Q01) and <c>HallucinationRateEvaluator</c> (AIR-Q04)
/// to compute rolling quality rates over a configurable sample window.
/// </para>
/// </summary>
public interface IAiMetricsReadRepository
{
    /// <summary>
    /// Returns the most recent <paramref name="windowSize"/> agreement metric events,
    /// ordered descending by record timestamp (AIR-Q01 rolling rate computation).
    /// </summary>
    /// <param name="windowSize">Maximum number of events to include in the rolling window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of <c>(TotalCount, AgreementCount)</c> where <c>TotalCount</c> is the number
    /// of events returned and <c>AgreementCount</c> is the subset where staff agreed with the AI.
    /// </returns>
    Task<(int TotalCount, int AgreementCount)> GetRecentAgreementEventsAsync(
        int windowSize,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent <paramref name="windowSize"/> verified field samples,
    /// ordered descending by record timestamp (AIR-Q04 rolling hallucination rate computation).
    /// </summary>
    /// <param name="windowSize">Maximum number of verified samples to include in the rolling window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of <c>(TotalVerified, HallucinatedCount)</c> where <c>TotalVerified</c> is the
    /// number of staff-verified samples and <c>HallucinatedCount</c> is the subset where the
    /// AI value diverged from ground truth.
    /// </returns>
    Task<(int TotalVerified, int HallucinatedCount)> GetRecentVerifiedSamplesAsync(
        int windowSize,
        CancellationToken ct = default);
    /// <summary>
    /// Returns the most recent <paramref name="windowSize"/> schema validity metric events,
    /// ordered descending by record timestamp (AIR-Q03 rolling rate computation).
    /// </summary>
    /// <param name="windowSize">Maximum number of events to include in the rolling window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of <c>(TotalCount, ValidCount)</c> where <c>TotalCount</c> is the number
    /// of schema validation events returned and <c>ValidCount</c> is the subset that passed
    /// schema validation.
    /// </returns>
    Task<(int TotalCount, int ValidCount)> GetRecentSchemaValidityEventsAsync(
        int windowSize,
        CancellationToken ct = default);
}

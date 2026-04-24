using Propel.Modules.AI.Interfaces;

namespace Propel.Modules.AI.Metrics;

/// <summary>
/// Null-object implementation of <see cref="IAiMetricsReadRepository"/> used when the
/// <c>AiQualityMetrics</c> persistence layer (task_002/task_003) is not yet available.
/// <para>
/// Returns zero-count tuples so evaluators correctly detect the "insufficient data" guard
/// (fewer than 50 verified samples) and suppress all rate alerts during this phase.
/// </para>
/// Replace this registration in <c>Program.cs</c> with the EF Core implementation
/// once <c>task_002_be_ai_metrics_api.md</c> is merged.
/// </summary>
public sealed class NullAiMetricsReadRepository : IAiMetricsReadRepository
{
    public Task<(int TotalCount, int AgreementCount)> GetRecentAgreementEventsAsync(
        int windowSize,
        CancellationToken ct = default)
        => Task.FromResult((0, 0));

    public Task<(int TotalVerified, int HallucinatedCount)> GetRecentVerifiedSamplesAsync(
        int windowSize,
        CancellationToken ct = default)
        => Task.FromResult((0, 0));

    public Task<(int TotalCount, int ValidCount)> GetRecentSchemaValidityEventsAsync(
        int windowSize,
        CancellationToken ct = default)
        => Task.FromResult((0, 0));
}

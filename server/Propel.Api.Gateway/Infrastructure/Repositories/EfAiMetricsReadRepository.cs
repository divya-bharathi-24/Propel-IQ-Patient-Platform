using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Modules.AI.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core read repository for AI quality metrics rolling-window queries
/// (EP-010/us_048, AC-1, AC-2, AC-3, task_002).
/// <para>
/// All queries use <c>.Take(windowSize).OrderByDescending()</c> — no raw SQL.
/// Implements <see cref="IAiMetricsReadRepository"/> to replace
/// <c>NullAiMetricsReadRepository</c> once this task is merged.
/// </para>
/// </summary>
public sealed class EfAiMetricsReadRepository : IAiMetricsReadRepository
{
    private readonly AppDbContext _context;

    public EfAiMetricsReadRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<(int TotalCount, int AgreementCount)> GetRecentAgreementEventsAsync(
        int windowSize,
        CancellationToken ct = default)
    {
        var events = await _context.AiQualityMetrics
            .Where(m => m.MetricType == "Agreement")
            .OrderByDescending(m => m.RecordedAt)
            .Take(windowSize)
            .Select(m => m.IsAgreement)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int total = events.Count;
        int agreed = events.Count(v => v == true);
        return (total, agreed);
    }

    /// <inheritdoc />
    public async Task<(int TotalVerified, int HallucinatedCount)> GetRecentVerifiedSamplesAsync(
        int windowSize,
        CancellationToken ct = default)
    {
        var events = await _context.AiQualityMetrics
            .Where(m => m.MetricType == "Hallucination")
            .OrderByDescending(m => m.RecordedAt)
            .Take(windowSize)
            .Select(m => m.IsHallucination)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int total = events.Count;
        int hallucinated = events.Count(v => v == true);
        return (total, hallucinated);
    }

    /// <inheritdoc />
    public async Task<(int TotalCount, int ValidCount)> GetRecentSchemaValidityEventsAsync(
        int windowSize,
        CancellationToken ct = default)
    {
        var events = await _context.AiQualityMetrics
            .Where(m => m.MetricType == "SchemaValidity")
            .OrderByDescending(m => m.RecordedAt)
            .Take(windowSize)
            .Select(m => m.IsSchemaValid)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int total = events.Count;
        int valid = events.Count(v => v == true);
        return (total, valid);
    }
}

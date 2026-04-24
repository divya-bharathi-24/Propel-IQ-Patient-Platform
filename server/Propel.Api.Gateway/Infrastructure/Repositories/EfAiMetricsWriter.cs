using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// INSERT-only EF Core implementation of <see cref="IAiMetricsWriter"/>
/// (EP-010/us_048, AC-1, AC-2, AC-3, task_002).
/// <para>
/// All three write methods issue a single INSERT via <c>AddAsync</c> + <c>SaveChangesAsync</c>.
/// No UPDATE or DELETE calls are ever made — consistent with the AD-7 append-only pattern.
/// </para>
/// <para>
/// Per NFR-018, metric write failures must not interrupt the primary request flow.
/// Callers should invoke these methods fire-and-forget or wrap in try/catch to enforce
/// graceful degradation. Exceptions are not swallowed here so callers can choose
/// their error-handling strategy.
/// </para>
/// </summary>
public sealed class EfAiMetricsWriter : IAiMetricsWriter
{
    private readonly AppDbContext _context;

    public EfAiMetricsWriter(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task WriteAgreementEventAsync(
        Guid sessionId,
        string fieldName,
        bool isAgreement,
        CancellationToken ct = default)
    {
        var entity = new AiQualityMetric
        {
            Id          = Guid.NewGuid(),
            SessionId   = sessionId,
            MetricType  = "Agreement",
            FieldName   = fieldName,
            IsAgreement = isAgreement,
            RecordedAt  = DateTimeOffset.UtcNow
        };

        await _context.AiQualityMetrics.AddAsync(entity, ct).ConfigureAwait(false);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        Log.Debug(
            "EfAiMetricsWriter_Agreement: sessionId={SessionId} field={FieldName} isAgreement={IsAgreement} recorded.",
            sessionId, fieldName, isAgreement);
    }

    /// <inheritdoc />
    public async Task WriteHallucinationEventAsync(
        Guid sessionId,
        string fieldName,
        CancellationToken ct = default)
    {
        var entity = new AiQualityMetric
        {
            Id               = Guid.NewGuid(),
            SessionId        = sessionId,
            MetricType       = "Hallucination",
            FieldName        = fieldName,
            IsHallucination  = true,
            RecordedAt       = DateTimeOffset.UtcNow
        };

        await _context.AiQualityMetrics.AddAsync(entity, ct).ConfigureAwait(false);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        Log.Debug(
            "EfAiMetricsWriter_Hallucination: sessionId={SessionId} field={FieldName} recorded.",
            sessionId, fieldName);
    }

    /// <inheritdoc />
    public async Task WriteSchemaValidityEventAsync(
        string functionName,
        bool isValid,
        CancellationToken ct = default)
    {
        // SchemaValidity events originate from SK kernel function invocations — no session ID
        // is available at that call site. Guid.Empty is used as a sentinel; functionName is
        // stored in FieldName for traceability.
        var entity = new AiQualityMetric
        {
            Id            = Guid.NewGuid(),
            SessionId     = Guid.Empty,
            MetricType    = "SchemaValidity",
            FieldName     = functionName,
            IsSchemaValid = isValid,
            RecordedAt    = DateTimeOffset.UtcNow
        };

        await _context.AiQualityMetrics.AddAsync(entity, ct).ConfigureAwait(false);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        Log.Debug(
            "EfAiMetricsWriter_SchemaValidity: function={FunctionName} isValid={IsValid} recorded.",
            functionName, isValid);
    }
}

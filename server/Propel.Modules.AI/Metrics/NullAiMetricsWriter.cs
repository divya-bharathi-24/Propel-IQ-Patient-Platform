using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Modules.AI.Metrics;

/// <summary>
/// Null-object implementation of <see cref="IAiMetricsWriter"/> used when the
/// <c>AiQualityMetrics</c> persistence layer (task_002/task_003) is not yet available.
/// <para>
/// All write operations are no-ops that emit a structured debug log entry.
/// Replace this registration in <c>Program.cs</c> with the EF Core implementation
/// once <c>task_002_be_ai_metrics_api.md</c> is merged.
/// </para>
/// Follows the null-object pattern established by <c>NullAiIntakeService</c> (US_028).
/// </summary>
public sealed class NullAiMetricsWriter : IAiMetricsWriter
{
    public Task WriteAgreementEventAsync(Guid sessionId, string fieldName, bool isAgreement, CancellationToken ct = default)
    {
        Log.Debug(
            "NullAiMetricsWriter_Agreement: sessionId={SessionId} field={FieldName} isAgreement={IsAgreement} — metrics persistence not yet configured (awaiting task_002).",
            sessionId, fieldName, isAgreement);
        return Task.CompletedTask;
    }

    public Task WriteHallucinationEventAsync(Guid sessionId, string fieldName, CancellationToken ct = default)
    {
        Log.Debug(
            "NullAiMetricsWriter_Hallucination: sessionId={SessionId} field={FieldName} — metrics persistence not yet configured (awaiting task_002).",
            sessionId, fieldName);
        return Task.CompletedTask;
    }

    public Task WriteSchemaValidityEventAsync(string functionName, bool isValid, CancellationToken ct = default)
    {
        Log.Debug(
            "NullAiMetricsWriter_SchemaValidity: function={FunctionName} isValid={IsValid} — metrics persistence not yet configured (awaiting task_002).",
            functionName, isValid);
        return Task.CompletedTask;
    }
}

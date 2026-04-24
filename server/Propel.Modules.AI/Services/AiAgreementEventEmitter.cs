using Propel.Domain.Enums;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Metrics;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Default implementation of <see cref="IAiAgreementEventEmitter"/> (us_048, AC-1, AIR-Q01).
/// <para>
/// Called by <c>ConfirmMedicalCodesCommandHandler</c> after each code confirmation or rejection.
/// Maps the <see cref="MedicalCodeVerificationStatus"/> to a boolean agreement flag, writes
/// the event via <see cref="IAiMetricsWriter"/>, and triggers rolling rate evaluation via
/// <see cref="AgreementRateEvaluator"/>.
/// </para>
/// <para>
/// Decision mapping:
/// <list type="bullet">
///   <item><description><see cref="MedicalCodeVerificationStatus.Accepted"/> → <c>isAgreement = true</c></description></item>
///   <item><description><see cref="MedicalCodeVerificationStatus.Modified"/> → <c>isAgreement = true</c> (staff refined, not rejected)</description></item>
///   <item><description><see cref="MedicalCodeVerificationStatus.Rejected"/> → <c>isAgreement = false</c></description></item>
///   <item><description><see cref="MedicalCodeVerificationStatus.Pending"/> → no event emitted (not yet reviewed)</description></item>
/// </list>
/// </para>
/// All exceptions are caught and logged — metric failures must never interrupt the
/// primary confirmation flow (NFR-018).
/// </summary>
public sealed class AiAgreementEventEmitter : IAiAgreementEventEmitter
{
    private readonly IAiMetricsWriter       _metricsWriter;
    private readonly AgreementRateEvaluator _rateEvaluator;

    public AiAgreementEventEmitter(
        IAiMetricsWriter metricsWriter,
        AgreementRateEvaluator rateEvaluator)
    {
        _metricsWriter = metricsWriter;
        _rateEvaluator = rateEvaluator;
    }

    /// <inheritdoc/>
    public async Task EmitAgreementEventAsync(
        Guid sessionId,
        string fieldName,
        MedicalCodeVerificationStatus decision,
        CancellationToken ct = default)
    {
        // Pending codes have not been reviewed yet; no agreement metric to record.
        if (decision == MedicalCodeVerificationStatus.Pending)
            return;

        bool isAgreement = decision is
            MedicalCodeVerificationStatus.Accepted or
            MedicalCodeVerificationStatus.Modified;

        try
        {
            await _metricsWriter
                .WriteAgreementEventAsync(sessionId, fieldName, isAgreement, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Metric write failure must never interrupt the confirmation workflow (NFR-018).
            Log.Warning(ex,
                "AiAgreementEventEmitter_WriteFailed: sessionId={SessionId} field={FieldName} — agreement event write failed.",
                sessionId, fieldName);
            return;
        }

        try
        {
            await _rateEvaluator.EvaluateAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Rate evaluation failure must never interrupt the confirmation workflow (NFR-018).
            Log.Warning(ex,
                "AiAgreementEventEmitter_EvaluateFailed: sessionId={SessionId} — agreement rate evaluation failed.",
                sessionId);
        }
    }
}

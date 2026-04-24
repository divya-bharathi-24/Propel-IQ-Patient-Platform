using Propel.Domain.Enums;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Emitter for AI-Human agreement metric events triggered when staff confirm or reject
/// an AI-suggested value (us_048, AC-1, AIR-Q01).
/// <para>
/// Called from <c>ConfirmMedicalCodesCommandHandler</c> after each code decision is persisted.
/// Internally maps the staff decision to a boolean agreement flag, writes the event via
/// <see cref="IAiMetricsWriter"/>, and triggers rolling rate evaluation via
/// <c>AgreementRateEvaluator</c>.
/// </para>
/// </summary>
public interface IAiAgreementEventEmitter
{
    /// <summary>
    /// Emits an agreement metric event for a single staff code decision.
    /// </summary>
    /// <param name="sessionId">
    /// The AI session or medical code record identifier linking the staff decision to the
    /// originating AI suggestion (used for metrics correlation).
    /// </param>
    /// <param name="fieldName">
    /// Human-readable label for the AI-suggested field (e.g. "ICD10Code", "CPTCode").
    /// Must not contain patient PII (AIR-S03).
    /// </param>
    /// <param name="decision">
    /// The staff verification decision. <see cref="MedicalCodeVerificationStatus.Accepted"/>
    /// and <see cref="MedicalCodeVerificationStatus.Modified"/> map to agreement = <c>true</c>;
    /// <see cref="MedicalCodeVerificationStatus.Rejected"/> maps to agreement = <c>false</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task EmitAgreementEventAsync(
        Guid sessionId,
        string fieldName,
        MedicalCodeVerificationStatus decision,
        CancellationToken ct = default);
}

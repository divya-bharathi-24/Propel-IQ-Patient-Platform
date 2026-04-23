using MediatR;

namespace Propel.Modules.Risk.Events;

/// <summary>
/// MediatR notification published by <c>CalculateNoShowRiskCommandHandler</c> immediately after
/// a <c>NoShowRisk</c> UPSERT (US_032, task_002, AC-2, AC-3, AC-4).
/// <para>
/// Consumed by <c>NoShowRiskAssessedEventHandler</c> (in the Gateway infrastructure layer) which:
/// <list type="bullet">
///   <item>Inserts <c>AdditionalReminder</c> + <c>CallbackRequest</c> intervention rows when
///         <c>Score &gt; 0.66</c> (High severity) and no Pending rows already exist (idempotent).</item>
///   <item>Auto-clears any existing Pending interventions when <c>Score ≤ 0.66</c> (edge case).</item>
/// </list>
/// </para>
/// <para>
/// AG-6 compliance: the handler wraps the entire body in try/catch and NEVER throws.
/// </para>
/// </summary>
/// <param name="AppointmentId">PK of the appointment that was scored.</param>
/// <param name="NoShowRiskId">PK of the upserted <c>NoShowRisk</c> record.</param>
/// <param name="Score">Calculated risk score in [0, 1].</param>
public sealed record NoShowRiskAssessedEvent(
    Guid AppointmentId,
    Guid NoShowRiskId,
    decimal Score
) : INotification;

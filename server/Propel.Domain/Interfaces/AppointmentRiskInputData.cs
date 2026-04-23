namespace Propel.Domain.Interfaces;

/// <summary>
/// Carries the raw data required by <c>INoShowRiskCalculator</c> to compute
/// a no-show risk score (us_031, task_002, AC-1, AC-3).
/// All fields are resolved by <c>INoShowRiskRepository.GetRiskInputDataAsync</c>
/// via EF Core; missing values are represented as <c>null</c> so the calculator
/// can apply neutral defaults (edge-case spec: missing data → factor score = 0.5).
/// </summary>
/// <param name="AppointmentId">Primary key of the appointment being scored.</param>
/// <param name="PatientId">FK to patient; <c>null</c> for anonymous walk-ins (neutral defaults apply).</param>
/// <param name="AppointmentDate">Scheduled date used to compute booking lead time.</param>
/// <param name="SpecialtyName">Display name of the specialty for type-factor lookup; <c>null</c> falls back to neutral.</param>
/// <param name="PriorNoShowCount">Number of previous no-show appointments for the patient; <c>null</c> = no history (neutral 0.5).</param>
/// <param name="IntakeCompleted">True = intake record exists and <c>CompletedAt</c> is set; false = record exists but not completed; null = no record.</param>
/// <param name="AnyNotificationDelivered">True when at least one notification for this appointment has <c>SentAt</c> not null.</param>
/// <param name="AnyNotificationSent">True when at least one notification record exists for this appointment (regardless of delivery).</param>
public sealed record AppointmentRiskInputData(
    Guid AppointmentId,
    Guid? PatientId,
    DateOnly AppointmentDate,
    string? SpecialtyName,
    int? PriorNoShowCount,
    bool? IntakeCompleted,
    bool AnyNotificationDelivered,
    bool AnyNotificationSent
);

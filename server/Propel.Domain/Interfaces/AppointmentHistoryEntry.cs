namespace Propel.Domain.Interfaces;

/// <summary>
/// A single historical appointment record used as behavioral context for the AI
/// no-show risk augmentation call (us_031, task_003, AIR-007, AIR-O01).
/// Instances are produced by <c>INoShowRiskRepository.GetPatientAppointmentHistoryAsync</c>
/// and trimmed to the last 24 months / max 50 records before being passed to the prompt builder.
/// </summary>
/// <param name="Date">Scheduled date of the historical appointment.</param>
/// <param name="Status">Appointment lifecycle state as a string (e.g. "Completed", "Cancelled", "NoShow").</param>
/// <param name="ReminderDelivered">True when at least one notification for this appointment has a <c>SentAt</c> timestamp.</param>
/// <param name="IntakeCompleted">True when the intake record for this appointment has a <c>CompletedAt</c> timestamp; false when incomplete; null when no record exists.</param>
public sealed record AppointmentHistoryEntry(
    DateOnly Date,
    string Status,
    bool ReminderDelivered,
    bool? IntakeCompleted
);

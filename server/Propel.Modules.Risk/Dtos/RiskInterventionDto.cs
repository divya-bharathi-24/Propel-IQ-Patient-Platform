namespace Propel.Modules.Risk.Dtos;

/// <summary>
/// Intervention row returned by <c>GET /api/risk/{appointmentId}/interventions</c> (US_032, AC-2, AC-3).
/// Includes all status values — Pending, Accepted, Dismissed, and AutoCleared — for history display.
/// </summary>
/// <param name="Id">PK of the <c>RiskIntervention</c> row.</param>
/// <param name="AppointmentId">PK of the associated appointment.</param>
/// <param name="Type">Intervention type string ("AdditionalReminder" or "CallbackRequest").</param>
/// <param name="Status">Current lifecycle status string.</param>
/// <param name="StaffId">Staff member who acknowledged; <c>null</c> while Pending or AutoCleared.</param>
/// <param name="AcknowledgedAt">UTC timestamp of acknowledgement; <c>null</c> while Pending or AutoCleared.</param>
/// <param name="DismissalReason">Optional dismissal reason provided by Staff; <c>null</c> unless Dismissed.</param>
public sealed record RiskInterventionDto(
    Guid Id,
    Guid AppointmentId,
    string Type,
    string Status,
    Guid? StaffId,
    DateTime? AcknowledgedAt,
    string? DismissalReason
);

namespace Propel.Modules.Risk.Dtos;

/// <summary>
/// Appointment row returned by <c>GET /api/risk/requires-attention</c> for the Staff
/// "Requires Attention" dashboard section (US_032, AC-4, AD-2 CQRS read model).
/// </summary>
/// <param name="AppointmentId">PK of the appointment.</param>
/// <param name="PatientName">Patient display name; "Walk-In Guest" for anonymous appointments.</param>
/// <param name="Date">Scheduled calendar date of the appointment.</param>
/// <param name="TimeSlotStart">Scheduled start time; <c>null</c> when no time slot is assigned.</param>
/// <param name="RiskScore">No-show risk score in [0, 1] — always &gt; 0.66 for items in this list.</param>
/// <param name="PendingInterventionCount">Number of Pending interventions for this appointment.</param>
public sealed record RequiresAttentionItemDto(
    Guid AppointmentId,
    string PatientName,
    DateOnly Date,
    TimeOnly? TimeSlotStart,
    decimal RiskScore,
    int PendingInterventionCount
);

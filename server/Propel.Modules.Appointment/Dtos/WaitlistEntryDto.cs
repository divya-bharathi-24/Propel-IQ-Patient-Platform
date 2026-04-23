namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Read DTO returned by <c>GET /api/waitlist/me</c> (US_023, AC-3).
/// Used by the patient dashboard to render the preferred-slot waitlist indicator.
/// </summary>
public sealed record WaitlistEntryDto(
    Guid Id,
    Guid CurrentAppointmentId,
    DateOnly PreferredDate,
    TimeOnly PreferredTimeSlot,
    DateTime EnrolledAt,
    string Status);

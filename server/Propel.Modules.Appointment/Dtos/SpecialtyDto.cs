namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Response DTO for a single specialty returned by <c>GET /api/appointments/specialties</c> (US_018).
/// </summary>
public sealed record SpecialtyDto(Guid Id, string Name);

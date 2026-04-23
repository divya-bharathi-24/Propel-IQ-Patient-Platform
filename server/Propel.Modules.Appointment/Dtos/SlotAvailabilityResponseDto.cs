namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Response envelope for <c>GET /api/appointments/slots</c> (US_018, AC-1, AC-4).
/// <para>
/// <c>Slots</c> contains every grid slot for the requested date; slots held by
/// <c>Booked</c> or <c>Arrived</c> appointments have <c>IsAvailable = false</c>.
/// When the list is fully unavailable the frontend infers the fully-booked state —
/// no separate status code is returned (AC-4).
/// </para>
/// </summary>
public sealed record SlotAvailabilityResponseDto(
    DateOnly Date,
    Guid SpecialtyId,
    IReadOnlyList<SlotDto> Slots);

namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Represents a single appointment time slot with its availability status (US_018, AC-1, AC-4).
/// <para>
/// <c>IsAvailable = false</c> indicates the slot is held by a <c>Booked</c> or <c>Arrived</c>
/// appointment. When all slots have <c>IsAvailable = false</c>, the frontend renders the
/// fully-booked state (AC-4).
/// </para>
/// <para>
/// <c>TimeSlotStart</c> and <c>TimeSlotEnd</c> are UTC <see cref="DateTimeOffset"/> values so
/// the frontend can parse them directly as ISO 8601 strings (e.g. "2026-05-01T09:00:00+00:00").
/// <c>SpecialtyId</c> and <c>Date</c> are included so the flat array response is self-describing
/// without a separate wrapper envelope.
/// </para>
/// </summary>
public sealed record SlotDto(
    DateTimeOffset TimeSlotStart,
    DateTimeOffset TimeSlotEnd,
    bool IsAvailable,
    Guid SpecialtyId,
    DateOnly Date);

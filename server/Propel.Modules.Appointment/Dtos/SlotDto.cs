namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Represents a single appointment time slot with its availability status (US_018, AC-1, AC-4).
/// <para>
/// <c>IsAvailable = false</c> indicates the slot is held by a <c>Booked</c> or <c>Arrived</c>
/// appointment. When all slots have <c>IsAvailable = false</c>, the frontend renders the
/// fully-booked state (AC-4).
/// </para>
/// </summary>
public sealed record SlotDto(TimeOnly TimeSlotStart, TimeOnly TimeSlotEnd, bool IsAvailable);

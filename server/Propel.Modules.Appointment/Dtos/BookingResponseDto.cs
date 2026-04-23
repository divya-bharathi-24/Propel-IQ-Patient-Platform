using Propel.Domain.Enums;

namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Response DTO returned by <c>POST /api/appointments/book</c> (US_019, AC-2).
/// Contains confirmation details shown on the booking success screen.
/// </summary>
public sealed record BookingResponseDto(
    Guid AppointmentId,
    string ReferenceNumber,
    DateOnly Date,
    TimeOnly TimeSlotStart,
    string SpecialtyName,
    InsuranceValidationResult InsuranceStatus);

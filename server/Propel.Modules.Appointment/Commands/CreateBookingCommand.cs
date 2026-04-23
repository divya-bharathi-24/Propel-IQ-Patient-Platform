using MediatR;
using Propel.Domain.Enums;
using Propel.Modules.Appointment.Dtos;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// Main booking command for <c>POST /api/appointments/book</c> (US_019, AC-2, AC-3; US_023, AC-1).
/// The <c>patientId</c> is resolved from JWT claims inside the handler — never from
/// the request body (OWASP A01). Dispatched by <c>BookingController</c> after
/// FluentValidation via <c>CreateBookingCommandValidator</c>.
/// <para>
/// When <c>PreferredDate</c> and <c>PreferredTimeSlot</c> are non-null, the handler
/// validates that the preferred slot is genuinely unavailable and inserts a
/// <see cref="Propel.Domain.Entities.WaitlistEntry"/> (US_023, AC-1, DR-003).
/// </para>
/// </summary>
public sealed record CreateBookingCommand(
    Guid SlotSpecialtyId,
    DateOnly SlotDate,
    TimeOnly SlotTimeStart,
    TimeOnly SlotTimeEnd,
    IntakeMode IntakeMode,
    string? InsuranceName,
    string? InsuranceId,
    DateOnly? PreferredDate,
    TimeOnly? PreferredTimeSlot) : IRequest<BookingResponseDto>;

using MediatR;
using Propel.Domain.Enums;
using Propel.Modules.Appointment.Dtos;
using System.Text.Json.Serialization;

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
public sealed record CreateBookingCommand : IRequest<BookingResponseDto>
{
    [JsonPropertyName("slotSpecialtyId")]
    public Guid SlotSpecialtyId { get; init; }

    [JsonPropertyName("slotDate")]
    public DateOnly SlotDate { get; init; }

    [JsonPropertyName("slotTimeStart")]
    public TimeOnly SlotTimeStart { get; init; }

    [JsonPropertyName("slotTimeEnd")]
    public TimeOnly SlotTimeEnd { get; init; }

    [JsonPropertyName("intakeMode")]
    public IntakeMode IntakeMode { get; init; }

    [JsonPropertyName("insuranceName")]
    public string? InsuranceName { get; init; }

    [JsonPropertyName("insuranceId")]
    public string? InsuranceId { get; init; }

    [JsonPropertyName("preferredDate")]
    public DateOnly? PreferredDate { get; init; }

    [JsonPropertyName("preferredTimeSlot")]
    public TimeOnly? PreferredTimeSlot { get; init; }
}

using MediatR;
using System.Text.Json.Serialization;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// Places a short-lived Redis slot-hold entry for <c>POST /api/appointments/hold-slot</c>
/// (US_019, AC-2). TTL = 300 seconds. The <c>patientId</c> is resolved from JWT claims
/// inside the handler — never from the request body (OWASP A01).
/// </summary>
public sealed record HoldSlotCommand : IRequest
{
    [JsonPropertyName("specialtyId")]
    public Guid SpecialtyId { get; init; }

    [JsonPropertyName("date")]
    public DateOnly Date { get; init; }

    [JsonPropertyName("timeSlotStart")]
    public TimeOnly TimeSlotStart { get; init; }
}

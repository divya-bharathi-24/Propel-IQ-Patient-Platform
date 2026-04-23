using MediatR;
using Propel.Modules.Appointment.Dtos;

namespace Propel.Modules.Appointment.Queries;

/// <summary>
/// MediatR query for <c>GET /api/appointments/slots</c> (US_018, AC-1).
/// <para>
/// Dispatched by <c>AppointmentController</c> after FluentValidation confirms
/// <c>SpecialtyId</c> is non-empty and <c>Date</c> is today or a future UTC date.
/// </para>
/// </summary>
public sealed record GetAvailableSlotsQuery(Guid SpecialtyId, DateOnly Date)
    : IRequest<SlotAvailabilityResponseDto>;

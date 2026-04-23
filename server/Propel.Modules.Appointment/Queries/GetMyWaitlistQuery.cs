using MediatR;
using Propel.Modules.Appointment.Dtos;

namespace Propel.Modules.Appointment.Queries;

/// <summary>
/// Returns all Active <see cref="Dtos.WaitlistEntryDto"/> records for the authenticated patient,
/// ordered by <c>enrolledAt</c> ascending (FIFO — US_023, AC-2, AC-3).
/// An empty list is a valid response when the patient has no active waitlist entries.
/// </summary>
public sealed record GetMyWaitlistQuery(Guid PatientId)
    : IRequest<IReadOnlyList<WaitlistEntryDto>>;

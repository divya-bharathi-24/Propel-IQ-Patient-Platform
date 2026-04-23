using MediatR;
using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Queries;

/// <summary>
/// MediatR query for <c>GET /api/staff/appointments/{id}</c> (US_034, AC-3).
/// Returns the appointment detail DTO with the <c>lastManualReminder</c> projection for
/// confirmation display in the staff UI after a manual reminder trigger.
/// </summary>
public sealed record GetStaffAppointmentDetailQuery(Guid AppointmentId)
    : IRequest<AppointmentDetailDto>;

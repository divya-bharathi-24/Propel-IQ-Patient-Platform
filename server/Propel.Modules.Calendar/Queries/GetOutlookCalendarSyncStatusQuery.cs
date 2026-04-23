using MediatR;
using Propel.Domain.Enums;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Queries;

/// <summary>
/// Returns the Outlook Calendar sync status for the requesting patient's appointment (us_036).
/// Returns <c>null</c> when no <c>CalendarSync</c> record exists for the given appointment
/// with <c>provider = Outlook</c>.
/// The handler validates <c>CalendarSync.patientId == requestingPatientId</c> (OWASP A01).
/// </summary>
public sealed record GetOutlookCalendarSyncStatusQuery(Guid AppointmentId)
    : IRequest<CalendarSyncStatusDto?>;

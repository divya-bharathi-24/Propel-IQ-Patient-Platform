using MediatR;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Queries;

/// <summary>
/// Returns the Google Calendar sync status for the requesting patient's appointment (us_035).
/// Returns <c>null</c> when no CalendarSync record exists for the given appointment.
/// The handler validates <c>CalendarSync.patientId == requestingPatientId</c> (OWASP A01).
/// </summary>
public sealed record GetCalendarSyncStatusQuery(Guid AppointmentId) : IRequest<CalendarSyncStatusDto?>;

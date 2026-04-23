using MediatR;
using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Commands;

/// <summary>
/// MediatR command for <c>POST /api/staff/appointments/{appointmentId}/reminders/trigger</c>
/// (US_034, AC-1, AC-2).
/// <para>
/// <c>StaffUserId</c> is resolved from the JWT <c>sub</c> claim inside the controller —
/// never accepted from the request body (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record TriggerManualReminderCommand(
    Guid AppointmentId,
    Guid StaffUserId) : IRequest<TriggerManualReminderResponseDto>;

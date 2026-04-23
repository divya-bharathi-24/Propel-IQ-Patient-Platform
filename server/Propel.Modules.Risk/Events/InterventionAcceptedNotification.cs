using MediatR;
using Propel.Domain.Enums;

namespace Propel.Modules.Risk.Events;

/// <summary>
/// MediatR notification published by <c>AcceptInterventionCommandHandler</c> after an
/// intervention is accepted by a Staff member (US_032, AC-2, FR-030).
/// <para>
/// Consumed by <c>InterventionAcceptedNotificationHandler</c> which triggers the relevant
/// downstream action based on <c>Type</c>:
/// <list type="bullet">
///   <item><c>AdditionalReminder</c> — triggers an ad-hoc patient reminder (stub; full
///         reminder integration delivered in EP-006/us_033).</item>
///   <item><c>CallbackRequest</c> — logs the callback request for Staff follow-up.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="AppointmentId">PK of the appointment the intervention belongs to.</param>
/// <param name="InterventionType">Type of the accepted intervention.</param>
/// <param name="StaffId">Staff member who accepted (sourced from JWT; OWASP A01).</param>
public sealed record InterventionAcceptedNotification(
    Guid AppointmentId,
    InterventionType InterventionType,
    Guid StaffId
) : INotification;

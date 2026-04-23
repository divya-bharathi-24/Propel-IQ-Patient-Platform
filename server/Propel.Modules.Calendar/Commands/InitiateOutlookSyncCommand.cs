using MediatR;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Commands;

/// <summary>
/// Initiates the Microsoft Outlook Calendar OAuth 2.0 PKCE flow for a patient's appointment
/// (us_036, AC-1).
/// <para>
/// The handler resolves <c>patientId</c> exclusively from JWT claims (OWASP A01),
/// encodes <c>state = Base64(appointmentId:patientId)</c> for CSRF protection,
/// and returns a Microsoft OAuth 2.0 authorization URL via MSAL.
/// </para>
/// </summary>
public sealed record InitiateOutlookSyncCommand(Guid AppointmentId)
    : IRequest<InitiateOutlookSyncResultDto>;

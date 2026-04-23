using MediatR;

namespace Propel.Modules.Calendar.Commands;

/// <summary>
/// Initiates the Google Calendar OAuth 2.0 flow for the given appointment (us_035, AC-1).
/// The handler generates PKCE verifier + challenge, stores state in Redis (10-min TTL),
/// and returns the Google authorization URL for a 302 redirect.
/// <para>
/// <c>patientId</c> is resolved inside the handler from <c>ClaimsPrincipal</c> (OWASP A01).
/// </para>
/// </summary>
public sealed record InitiateGoogleSyncCommand(Guid AppointmentId) : IRequest<string>;

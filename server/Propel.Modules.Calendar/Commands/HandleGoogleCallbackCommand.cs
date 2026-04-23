using MediatR;

namespace Propel.Modules.Calendar.Commands;

/// <summary>
/// Handles the Google OAuth 2.0 callback (us_035, AC-2, AC-3, AC-4).
/// <list type="bullet">
///   <item>If <paramref name="Error"/> is "access_denied" → no sync record, redirect declined (AC-3).</item>
///   <item>Validates <paramref name="State"/> against Redis (OWASP A07).</item>
///   <item>Exchanges <paramref name="Code"/> for tokens, encrypts and stores them.</item>
///   <item>Creates or updates the Google Calendar event, upserts CalendarSync.</item>
///   <item>On <see cref="HttpRequestException"/>: CalendarSync Failed + retryScheduledAt (AC-4).</item>
///   <item>On <see cref="Exceptions.GoogleTokenExpiredException"/>: CalendarSync Revoked.</item>
/// </list>
/// Returns the frontend redirect URL with the result query parameter.
/// </summary>
public sealed record HandleGoogleCallbackCommand(
    string? Code,
    string? State,
    string? Error) : IRequest<string>;

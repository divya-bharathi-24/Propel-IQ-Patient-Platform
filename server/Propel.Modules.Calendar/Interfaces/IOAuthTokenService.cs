using Propel.Domain.Enums;

namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Abstracts OAuth 2.0 access-token acquisition and silent refresh for calendar providers
/// (us_037, EC-1, OWASP A02).
/// <para>
/// Tokens are stored encrypted in <c>PatientOAuthToken</c> via ASP.NET Core Data Protection.
/// This service decrypts, returns, and refreshes them as needed — the caller never handles
/// raw credentials.
/// </para>
/// </summary>
public interface IOAuthTokenService
{
    /// <summary>
    /// Returns the current decrypted access token for the given patient and provider.
    /// Proactively refreshes if the token expires within the next 60 seconds.
    /// Returns <c>null</c> if no token record exists or refresh fails.
    /// </summary>
    Task<string?> GetAccessTokenAsync(
        Guid patientId,
        CalendarProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces an OAuth 2.0 refresh-token grant, updates the stored <c>PatientOAuthToken</c>
    /// with the new encrypted access token, and returns the new plaintext access token.
    /// Returns <c>null</c> if the refresh fails (e.g. token revoked by patient) — caller
    /// should mark <c>CalendarSync.syncStatus = Failed</c> and prompt reconnect (EC-1).
    /// </summary>
    Task<string?> RefreshTokenAsync(
        Guid patientId,
        CalendarProvider provider,
        CancellationToken cancellationToken = default);
}

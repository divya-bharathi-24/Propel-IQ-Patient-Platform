namespace Propel.Domain.Interfaces;

/// <summary>
/// Short-lived re-authentication token store used to gate destructive Admin actions
/// (role elevation to Admin, account deactivation) behind a second-factor password proof
/// (US_046, FR-062, AD-8).
/// <para>
/// Tokens are single-use and expire after 5 minutes. The raw token is returned to the caller
/// after issuance; internally, only a SHA-256 hash of the token is persisted so that a Redis
/// dump cannot be replayed (OWASP A02).
/// </para>
/// </summary>
public interface IReAuthTokenStore
{
    /// <summary>
    /// Issues a cryptographically secure, single-use re-auth token bound to
    /// <paramref name="adminId"/>. The token is stored (as a SHA-256 hash) with a
    /// 5-minute TTL and is automatically invalidated on first consumption.
    /// </summary>
    /// <param name="adminId">The Admin user who performed successful re-authentication.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    /// <returns>The raw (unhashed) token string to return to the client.</returns>
    Task<string> IssueTokenAsync(Guid adminId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically validates and consumes a re-auth token. The token is permanently
    /// deleted from the store on first call (single-use enforcement).
    /// </summary>
    /// <param name="token">The raw token provided by the client.</param>
    /// <param name="expectedAdminId">
    /// The Admin whose identity must match the stored binding.
    /// Returns <c>false</c> when the stored admin ID does not match, preventing
    /// token theft across admin sessions.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    /// <returns>
    /// <c>true</c> if the token existed, was not expired, and matched
    /// <paramref name="expectedAdminId"/>; <c>false</c> otherwise.
    /// </returns>
    Task<bool> ConsumeTokenAsync(string token, Guid expectedAdminId, CancellationToken cancellationToken = default);
}

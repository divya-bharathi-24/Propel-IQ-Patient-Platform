namespace Propel.Domain.Interfaces;

/// <summary>
/// Abstraction for invalidating all active Redis sessions for a given user.
/// Deletes all keys matching <c>session:{userId}:*</c> to force immediate logout
/// on the next request (US_045, AC-3, AD-9).
/// </summary>
public interface ISessionInvalidationService
{
    /// <summary>
    /// Invalidates all active sessions for the specified user by removing
    /// all matching Redis session keys. No-op if the user has no active sessions.
    /// </summary>
    Task InvalidateAllSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

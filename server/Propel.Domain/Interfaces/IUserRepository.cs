using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="User"/> persistence (Staff and Admin accounts).
/// Patients authenticate through <see cref="IPatientRepository"/>; this interface handles
/// staff/admin account lifecycle only (US_012).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Returns <c>true</c> when a user with the given email already exists
    /// (case-insensitive). Used for duplicate-email detection (AC-1).
    /// </summary>
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Creates a new user record and returns the persisted entity.</summary>
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a user by primary key. Returns <c>null</c> when not found.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user's password hash and the credential email status.
    /// Used when credentials are set up via the token-gated endpoint (AC-2).
    /// </summary>
    Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the <c>CredentialEmailStatus</c> field on the user record.
    /// Called after a SendGrid dispatch attempt to record delivery state.
    /// </summary>
    Task UpdateCredentialEmailStatusAsync(User user, string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all users with role <c>Staff</c> or <c>Admin</c> ordered by name ascending.
    /// Excludes <c>Patient</c>-role accounts (US_045, AC-1).
    /// </summary>
    Task<List<User>> GetManagedUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a name and/or role change on an existing user record (US_045, AC-2 PATCH).
    /// </summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the user by setting <c>Status = Deactivated</c> (US_045, AC-3 DELETE, DR-010).
    /// </summary>
    Task DeactivateAsync(User user, CancellationToken cancellationToken = default);
}

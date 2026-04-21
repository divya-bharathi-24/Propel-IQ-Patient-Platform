namespace Propel.Domain.Interfaces;

/// <summary>
/// Centralised password hashing contract using Argon2id (NFR-008, DRY).
/// Single source of truth for password hashing across all modules
/// (<see cref="RegisterPatientCommandHandler"/>, <see cref="SetupCredentialsCommandHandler"/>).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes <paramref name="plaintext"/> using Argon2id with a cryptographically
    /// random 16-byte salt. The returned string encodes algorithm ID, parameters,
    /// salt, and hash — safe to store directly in <c>password_hash</c> columns.
    /// </summary>
    /// <param name="plaintext">The plaintext password to hash.</param>
    /// <returns>Argon2id encoded hash string.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plaintext"/> is null or empty.</exception>
    string Hash(string plaintext);

    /// <summary>
    /// Verifies <paramref name="plaintext"/> against a previously computed
    /// <paramref name="hash"/>. Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    /// <param name="plaintext">The plaintext password to verify.</param>
    /// <param name="hash">The Argon2id encoded hash to compare against.</param>
    /// <returns><c>true</c> if the password matches the hash; otherwise <c>false</c>.</returns>
    bool Verify(string plaintext, string hash);
}

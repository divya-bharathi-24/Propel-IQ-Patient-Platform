namespace Propel.Domain.Interfaces;

/// <summary>
/// Application-layer AES-256 encryption contract for PHI (Protected Health Information)
/// string fields (<c>Patient.Name</c>, <c>Patient.Phone</c>, <c>Patient.DateOfBirth</c>).
/// Satisfies NFR-004 (AES-256 at rest) and NFR-013 (HIPAA PHI handling).
///
/// <para>
/// Implementations encrypt before data reaches EF Core and decrypt transparently on read
/// via EF Core value converters defined in <c>AppDbContext.OnModelCreating</c>.
/// Key management, rotation, and persistence are handled by ASP.NET Core Data Protection API.
/// </para>
/// </summary>
public interface IPhiEncryptionService
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> using AES-256.
    /// Returns a Base64-encoded ciphertext safe for storage in a database column.
    /// </summary>
    /// <param name="plaintext">The PHI string to encrypt. Must not be null.</param>
    /// <returns>Base64-encoded ciphertext string.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a Base64-encoded ciphertext previously produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="ciphertext">The Base64-encoded ciphertext to decrypt. Must not be null.</param>
    /// <returns>The original plaintext PHI string.</returns>
    string Decrypt(string ciphertext);
}

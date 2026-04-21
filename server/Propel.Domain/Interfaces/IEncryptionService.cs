namespace Propel.Domain.Interfaces;

/// <summary>
/// Provides AES-256 symmetric encryption and decryption for PHI (Protected Health Information)
/// fields using PostgreSQL's pgcrypto extension (<c>pgp_sym_encrypt</c> / <c>pgp_sym_decrypt</c>).
///
/// <para>
/// <b>Usage pattern (Patient repository example):</b>
/// <code>
/// // Encrypt before write
/// patient.Phone = _encryption.Encrypt(rawPhone);
/// patient.DateOfBirth = _encryption.Encrypt(rawDob);
/// patient.Address = _encryption.Encrypt(rawAddress);
/// await _context.SaveChangesAsync();
///
/// // Decrypt after read
/// var phone = _encryption.Decrypt(patient.Phone);
/// </code>
/// </para>
///
/// <para>
/// The encryption key is sourced exclusively from the <c>ENCRYPTION_KEY</c> environment
/// variable (OWASP A02 — no hardcoded secrets). The implementation is registered as
/// <c>Singleton</c> in DI. Primary consumer is the <c>Patient</c> repository; PHI fields
/// are <c>DateOfBirth</c>, <c>Phone</c>, and <c>Address</c>.
/// </para>
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> using AES-256 via pgcrypto's
    /// <c>pgp_sym_encrypt</c> function and returns a Base64-encoded ciphertext string.
    /// </summary>
    /// <param name="plaintext">The sensitive string value to encrypt.</param>
    /// <returns>Base64-encoded ciphertext suitable for storage in a TEXT database column.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a Base64-encoded <paramref name="ciphertext"/> produced by <see cref="Encrypt"/>
    /// using pgcrypto's <c>pgp_sym_decrypt</c> function and returns the original plaintext.
    /// </summary>
    /// <param name="ciphertext">Base64-encoded ciphertext as stored in the database.</param>
    /// <returns>The original plaintext string.</returns>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Gets a value indicating whether the pgcrypto PostgreSQL extension is installed and
    /// available. Evaluated once at service construction and cached.
    /// Used by the startup health check to fail fast (see <c>Program.cs</c>).
    /// </summary>
    bool IsAvailable { get; }
}

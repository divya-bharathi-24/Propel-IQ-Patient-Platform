namespace Propel.Domain.Interfaces;

/// <summary>
/// Provides AES-256 file-level encryption for clinical documents uploaded to the platform.
/// Implementations use ASP.NET Core Data Protection API key ring (OWASP A02, NFR-004, FR-043).
/// Registered as <c>Singleton</c> in DI.
/// </summary>
public interface IFileEncryptionService
{
    /// <summary>
    /// Encrypts the given file bytes using AES-256.
    /// </summary>
    /// <param name="fileBytes">Raw file content to encrypt.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    /// <returns>Encrypted byte array ready for storage.</returns>
    Task<byte[]> EncryptAsync(byte[] fileBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts previously encrypted file bytes.
    /// </summary>
    /// <param name="encryptedBytes">Encrypted file content from storage.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    /// <returns>Decrypted raw file bytes.</returns>
    Task<byte[]> DecryptAsync(byte[] encryptedBytes, CancellationToken cancellationToken = default);
}

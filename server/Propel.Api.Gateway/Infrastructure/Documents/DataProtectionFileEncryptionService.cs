using Microsoft.AspNetCore.DataProtection;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Documents;

/// <summary>
/// AES-256 file-level encryption service using ASP.NET Core Data Protection API key ring.
/// Uses a dedicated purpose string <c>"ClinicalDocuments.v1"</c> so clinical document keys
/// are isolated from other Data Protection keys in the ring (OWASP A02, NFR-004, FR-043).
/// Registered as <c>Singleton</c> in DI via <c>Program.cs</c>.
/// </summary>
public sealed class DataProtectionFileEncryptionService : IFileEncryptionService
{
    private readonly IDataProtector _protector;

    public DataProtectionFileEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        // Purpose string scopes the key to clinical document encryption only (OWASP A02).
        _protector = dataProtectionProvider.CreateProtector("ClinicalDocuments.v1");
    }

    /// <inheritdoc />
    public Task<byte[]> EncryptAsync(byte[] fileBytes, CancellationToken cancellationToken = default)
    {
        var encrypted = _protector.Protect(fileBytes);
        return Task.FromResult(encrypted);
    }

    /// <inheritdoc />
    public Task<byte[]> DecryptAsync(byte[] encryptedBytes, CancellationToken cancellationToken = default)
    {
        var decrypted = _protector.Unprotect(encryptedBytes);
        return Task.FromResult(decrypted);
    }
}

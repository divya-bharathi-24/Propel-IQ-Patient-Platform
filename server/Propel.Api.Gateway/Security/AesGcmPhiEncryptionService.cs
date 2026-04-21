using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Security;

/// <summary>
/// AES-256 PHI encryption service backed by ASP.NET Core Data Protection API
/// (NFR-004, NFR-013, HIPAA 45 CFR §164.312(a)(2)(iv)).
///
/// <para>
/// Uses <see cref="IDataProtector.Protect(byte[])"/> / <see cref="IDataProtector.Unprotect(byte[])"/>
/// which internally applies AEAD (Authenticated Encryption with Associated Data) using AES-256.
/// Key management — rotation, persistence to file system (dev) or Upstash Redis (prod) — is
/// fully delegated to the ASP.NET Core Data Protection key ring configured in <c>Program.cs</c>.
/// All historical keys are retained in the ring so previously encrypted DB values can always
/// be decrypted after key rotation.
/// </para>
///
/// <para>
/// Registered as <c>Singleton</c> in DI. Consumed via EF Core value converters registered
/// in <c>AppDbContext.OnModelCreating</c> for <c>Patient.Name</c>, <c>Patient.Phone</c>,
/// and <c>Patient.DateOfBirth</c>.
/// </para>
/// </summary>
public sealed class AesGcmPhiEncryptionService : IPhiEncryptionService
{
    private const string Purpose = "phi-fields";
    private readonly IDataProtector _protector;

    public AesGcmPhiEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] protectedBytes = _protector.Protect(plaintextBytes);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        byte[] protectedBytes = Convert.FromBase64String(ciphertext);
        byte[] plaintextBytes = _protector.Unprotect(protectedBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }
}

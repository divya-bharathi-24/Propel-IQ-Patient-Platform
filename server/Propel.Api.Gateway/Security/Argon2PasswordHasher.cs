using Isopoh.Cryptography.Argon2;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Security;

/// <summary>
/// Argon2id password hasher (NFR-008, OWASP Password Storage Cheat Sheet 2024).
/// Parameters tuned for the interactive-login baseline on a single free-tier CPU core:
/// <list type="bullet">
///   <item>Memory: 19 456 KiB (19 MiB)</item>
///   <item>Iterations (time cost): 2</item>
///   <item>Parallelism: 1 (single-threaded, suitable for free-tier Railway instances)</item>
/// </list>
/// Salt is generated automatically by <see cref="Argon2.Hash"/> using a CSPRNG.
/// The encoded output string contains algorithm ID, parameters, salt, and hash — store verbatim.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int TimeCost = 2;
    private const int MemoryCost = 19_456; // 19 MiB — OWASP interactive baseline
    private const int Parallelism = 1;

    /// <inheritdoc />
    public string Hash(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Password cannot be null or empty.", nameof(plaintext));

        return Argon2.Hash(
            plaintext,
            timeCost: TimeCost,
            memoryCost: MemoryCost,
            parallelism: Parallelism,
            type: Argon2Type.HybridAddressing); // Argon2id
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="Argon2.Verify"/> which performs a constant-time comparison
    /// to prevent timing-oracle attacks (OWASP A07).
    /// </remarks>
    public bool Verify(string plaintext, string hash)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Password cannot be null or empty.", nameof(plaintext));

        return Argon2.Verify(hash, plaintext);
    }
}

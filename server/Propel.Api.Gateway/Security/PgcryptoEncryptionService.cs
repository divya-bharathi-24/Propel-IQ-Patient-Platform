using Microsoft.Extensions.Configuration;
using Npgsql;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Security;

/// <summary>
/// AES-256 symmetric encryption service backed by PostgreSQL's pgcrypto extension.
/// Wraps <c>pgp_sym_encrypt</c> / <c>pgp_sym_decrypt</c> via raw <see cref="NpgsqlDataSource"/>
/// scalar queries — intentionally NOT through EF Core value converters, which cannot
/// receive the runtime encryption key from DI.
/// </summary>
/// <remarks>
/// Registered as <c>Singleton</c>. Constructor performs a fail-fast check:
/// <list type="bullet">
///   <item>Throws <see cref="InvalidOperationException"/> if <c>ENCRYPTION_KEY</c> env var is absent.</item>
///   <item>Queries <c>pg_extension</c> on startup and caches the <see cref="IsAvailable"/> result.</item>
/// </list>
/// </remarks>
public sealed class PgcryptoEncryptionService : IEncryptionService
{
    private readonly string _key;
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _isAvailable;

    /// <summary>
    /// Initialises the service.
    /// </summary>
    /// <param name="config">ASP.NET Core configuration — must expose <c>ENCRYPTION_KEY</c>.</param>
    /// <param name="dataSource">Shared Npgsql data source used for raw scalar queries.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>ENCRYPTION_KEY</c> is null or empty (OWASP A02 fail-fast guard).
    /// </exception>
    public PgcryptoEncryptionService(IConfiguration config, NpgsqlDataSource dataSource)
    {
        var key = config["ENCRYPTION_KEY"];
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                "ENCRYPTION_KEY environment variable is required. " +
                "Set it in Railway (production) or .env (local development). " +
                "Never hardcode this value (OWASP A02).");

        _key = key;
        _dataSource = dataSource;
        _isAvailable = CheckPgcryptoAvailable();
    }

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <inheritdoc />
    /// <remarks>
    /// Executes: <c>SELECT encode(pgp_sym_encrypt($1::text, $2::text, 'cipher-algo=aes256'), 'base64')</c>
    /// </remarks>
    public string Encrypt(string plaintext)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT encode(pgp_sym_encrypt($1::text, $2::text, 'cipher-algo=aes256'), 'base64')";
        cmd.Parameters.AddWithValue(plaintext);
        cmd.Parameters.AddWithValue(_key);

        var result = cmd.ExecuteScalar();
        return result as string
               ?? throw new InvalidOperationException(
                   "pgp_sym_encrypt returned NULL. Ensure the pgcrypto extension is active.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Executes: <c>SELECT pgp_sym_decrypt(decode($1::text, 'base64'), $2::text)</c>
    /// </remarks>
    public string Decrypt(string ciphertext)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT pgp_sym_decrypt(decode($1::text, 'base64'), $2::text)";
        cmd.Parameters.AddWithValue(ciphertext);
        cmd.Parameters.AddWithValue(_key);

        var result = cmd.ExecuteScalar();
        return result as string
               ?? throw new InvalidOperationException(
                   "pgp_sym_decrypt returned NULL. Ciphertext may be malformed or the key incorrect.");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Queries <c>pg_extension</c> to confirm pgcrypto is installed.
    /// Result is cached in <see cref="_isAvailable"/> at construction time.
    /// </summary>
    private bool CheckPgcryptoAvailable()
    {
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*)::int FROM pg_extension WHERE extname = 'pgcrypto'";
            var count = cmd.ExecuteScalar();
            return count is int n && n > 0;
        }
        catch
        {
            // If the DB is unreachable at startup (e.g., integration test bootstrap order),
            // return false — the health check will surface this without crashing the process.
            return false;
        }
    }
}

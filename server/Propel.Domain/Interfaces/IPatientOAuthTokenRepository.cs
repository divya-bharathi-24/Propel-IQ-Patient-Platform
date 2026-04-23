using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="PatientOAuthToken"/> upsert and read operations (us_035, NFR-004).
/// Implementations live in Propel.Api.Gateway. Tokens are stored pre-encrypted by the caller.
/// All queries are parameterised (OWASP A03).
/// </summary>
public interface IPatientOAuthTokenRepository
{
    /// <summary>
    /// Returns the <see cref="PatientOAuthToken"/> for a given (patientId, provider) pair,
    /// or <c>null</c> if none exists.
    /// </summary>
    Task<PatientOAuthToken?> GetAsync(
        Guid patientId,
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new <see cref="PatientOAuthToken"/> record or updates the existing one
    /// (matched by patientId + provider) in a single <c>SaveChangesAsync</c>.
    /// The caller is responsible for encrypting token values before calling this method (NFR-004).
    /// </summary>
    Task UpsertAsync(
        PatientOAuthToken token,
        CancellationToken cancellationToken = default);
}

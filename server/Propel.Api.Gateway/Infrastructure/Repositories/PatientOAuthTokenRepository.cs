using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPatientOAuthTokenRepository"/> (us_035, task_002).
/// Token values are stored pre-encrypted by the handler — this repository performs
/// no encryption itself (Single Responsibility Principle, NFR-004).
/// All queries use parameterised LINQ — no raw string interpolation into SQL (OWASP A03).
/// </summary>
public sealed class PatientOAuthTokenRepository : IPatientOAuthTokenRepository
{
    private readonly AppDbContext _db;

    public PatientOAuthTokenRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<PatientOAuthToken?> GetAsync(
        Guid patientId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        return await _db.PatientOAuthTokens
            .FirstOrDefaultAsync(
                t => t.PatientId == patientId && t.Provider == provider,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        PatientOAuthToken token,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.PatientOAuthTokens
            .FirstOrDefaultAsync(
                t => t.PatientId == token.PatientId && t.Provider == token.Provider,
                cancellationToken);

        if (existing is null)
        {
            token.CreatedAt = DateTime.UtcNow;
            token.UpdatedAt = DateTime.UtcNow;
            await _db.PatientOAuthTokens.AddAsync(token, cancellationToken);
        }
        else
        {
            existing.EncryptedAccessToken  = token.EncryptedAccessToken;
            existing.EncryptedRefreshToken = token.EncryptedRefreshToken;
            existing.ExpiresAt             = token.ExpiresAt;
            existing.UpdatedAt             = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}

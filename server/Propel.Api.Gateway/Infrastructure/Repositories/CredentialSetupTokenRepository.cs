using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICredentialSetupTokenRepository"/>.
/// Token lookups are performed exclusively on the SHA-256 hex hash —
/// the raw token is never stored in the database (NFR-008).
/// </summary>
public sealed class CredentialSetupTokenRepository : ICredentialSetupTokenRepository
{
    private readonly AppDbContext _context;

    public CredentialSetupTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(
        CredentialSetupToken token,
        CancellationToken cancellationToken = default)
    {
        _context.CredentialSetupTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<CredentialSetupToken?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => _context.CredentialSetupTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task InvalidatePendingTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var pendingTokens = await _context.CredentialSetupTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var token in pendingTokens)
            token.UsedAt = now;

        if (pendingTokens.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets <c>UsedAt = UtcNow</c> on the tracked entity (in-memory mutation).
    /// The caller must trigger <c>SaveChangesAsync</c> via
    /// <see cref="IUserRepository.UpdatePasswordHashAsync"/> to persist atomically.
    /// </summary>
    public void MarkAsUsed(CredentialSetupToken token)
        => token.UsedAt = DateTime.UtcNow;
}

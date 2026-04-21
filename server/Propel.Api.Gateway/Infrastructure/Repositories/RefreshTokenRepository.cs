using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRefreshTokenRepository"/>.
/// Atomic rotation (<see cref="RotateAsync"/>) uses a single <c>SaveChangesAsync</c> call so both
/// the revocation and the insertion are committed in one database transaction (US_011, AC-3).
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task RevokeAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        token.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeTokenFamilyAsync(Guid familyId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _context.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now),
                cancellationToken);
    }

    public async Task RotateAsync(RefreshToken old, RefreshToken next, CancellationToken cancellationToken = default)
    {
        old.RevokedAt = DateTime.UtcNow;
        _context.RefreshTokens.Add(next);
        // Single SaveChangesAsync — EF Core wraps both operations in one implicit transaction
        await _context.SaveChangesAsync(cancellationToken);
    }
}

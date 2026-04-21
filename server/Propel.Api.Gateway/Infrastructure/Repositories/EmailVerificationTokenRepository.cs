using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IEmailVerificationTokenRepository"/>.
/// Token lookups are performed exclusively on the SHA-256 hex hash —
/// the raw token is never stored in the database (NFR-008).
/// </summary>
public sealed class EmailVerificationTokenRepository : IEmailVerificationTokenRepository
{
    private readonly AppDbContext _context;

    public EmailVerificationTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<EmailVerificationToken?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => _context.EmailVerificationTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task CreateAsync(
        EmailVerificationToken token,
        CancellationToken cancellationToken = default)
    {
        _context.EmailVerificationTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidatePendingTokensAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var pendingTokens = await _context.EmailVerificationTokens
            .Where(t => t.PatientId == patientId && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var token in pendingTokens)
            token.UsedAt = now;

        if (pendingTokens.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets <c>UsedAt = UtcNow</c> on the tracked entity.
    /// The caller is responsible for triggering <c>SaveChangesAsync</c>
    /// (via <see cref="IPatientRepository.MarkEmailVerifiedAsync"/>).
    /// </summary>
    public void MarkAsUsed(EmailVerificationToken token)
        => token.UsedAt = DateTime.UtcNow;
}

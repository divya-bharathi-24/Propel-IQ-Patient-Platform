using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUserRepository"/>.
/// Email lookups use <c>ToLowerInvariant()</c> to guarantee case-insensitive matching
/// without a functional index dependency (mirrors the PatientRepository pattern).
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Users
            .AnyAsync(u => u.Email == email.ToLowerInvariant(), cancellationToken);

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task UpdatePasswordHashAsync(
        User user,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        user.PasswordHash = passwordHash;
        // SaveChangesAsync persists all pending tracked changes atomically,
        // including CredentialSetupToken.UsedAt mutations in the same scope.
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCredentialEmailStatusAsync(
        User user,
        string status,
        CancellationToken cancellationToken = default)
    {
        user.CredentialEmailStatus = status;
        await _context.SaveChangesAsync(cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPatientRepository"/>.
/// Email lookups use PostgreSQL <c>lower()</c> via EF Core's <c>ToLower()</c>
/// to guarantee case-insensitive matching without a functional index dependency.
/// </summary>
public sealed class PatientRepository : IPatientRepository
{
    private readonly AppDbContext _context;

    public PatientRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Patients
            .AnyAsync(p => p.Email == email.ToLowerInvariant(), cancellationToken);

    public async Task<Patient> CreateAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync(cancellationToken);
        return patient;
    }

    public Task<Patient?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Patients
            .FirstOrDefaultAsync(p => p.Email == email.ToLowerInvariant(), cancellationToken);

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Patients
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task MarkEmailVerifiedAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        patient.EmailVerified = true;
        // SaveChangesAsync persists all pending tracked changes atomically,
        // including any EmailVerificationToken.UsedAt mutations made in the same scope.
        await _context.SaveChangesAsync(cancellationToken);
    }
}

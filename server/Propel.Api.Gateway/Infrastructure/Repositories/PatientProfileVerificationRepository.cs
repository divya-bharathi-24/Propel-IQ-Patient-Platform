using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPatientProfileVerificationRepository"/> (AC-3, task_002).
/// One record per patient — upserted on each successful verify call.
/// </summary>
public sealed class PatientProfileVerificationRepository : IPatientProfileVerificationRepository
{
    private readonly AppDbContext _context;

    public PatientProfileVerificationRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<PatientProfileVerification?> GetByPatientIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PatientProfileVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.PatientId == patientId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        PatientProfileVerification verification,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.PatientProfileVerifications
            .FirstOrDefaultAsync(v => v.PatientId == verification.PatientId, cancellationToken);

        if (existing is null)
        {
            _context.PatientProfileVerifications.Add(verification);
        }
        else
        {
            existing.Status = verification.Status;
            existing.VerifiedBy = verification.VerifiedBy;
            existing.VerifiedAt = verification.VerifiedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

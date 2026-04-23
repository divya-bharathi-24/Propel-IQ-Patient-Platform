using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IWaitlistRepository"/> (US_023, AC-2, AC-3, AC-4).
/// Provides read and update operations for <see cref="WaitlistEntry"/> records.
/// </summary>
public sealed class WaitlistRepository : IWaitlistRepository
{
    private readonly AppDbContext _db;

    public WaitlistRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WaitlistEntry>> GetActiveByPatientIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _db.WaitlistEntries
            .AsNoTracking()
            .Where(w => w.PatientId == patientId && w.Status == WaitlistStatus.Active)
            .OrderBy(w => w.EnrolledAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WaitlistEntry?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}

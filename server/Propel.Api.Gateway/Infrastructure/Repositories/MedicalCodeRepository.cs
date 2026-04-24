using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IMedicalCodeRepository"/> (EP-008-II/us_043, task_002).
/// <para>
/// Uses the request-scoped <see cref="AppDbContext"/> so that accept/reject mutations and
/// manual-entry inserts share the same EF change-tracker, enabling batched saves and
/// preventing over-posting via explicit PatientId filtering (OWASP A01).
/// </para>
/// <para>
/// All queries use parameterised LINQ — no raw SQL (OWASP A03).
/// </para>
/// </summary>
public sealed class MedicalCodeRepository : IMedicalCodeRepository
{
    private readonly AppDbContext _db;

    public MedicalCodeRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<List<MedicalCode>> GetByIdsAndPatientAsync(
        Guid patientId,
        IReadOnlySet<Guid> ids,
        CancellationToken cancellationToken)
    {
        // PatientId filter prevents Staff from modifying codes belonging to another patient
        // even if they supply a valid code ID (OWASP A01: Broken Access Control).
        return _db.MedicalCodes
            .Where(m => m.PatientId == patientId && ids.Contains(m.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(MedicalCode code, CancellationToken cancellationToken)
    {
        await _db.MedicalCodes.AddAsync(code, cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountPendingAsync(Guid patientId, CancellationToken cancellationToken)
    {
        return _db.MedicalCodes
            .AsNoTracking()
            .CountAsync(
                m => m.PatientId == patientId
                  && m.VerificationStatus == MedicalCodeVerificationStatus.Pending,
                cancellationToken);
    }
}

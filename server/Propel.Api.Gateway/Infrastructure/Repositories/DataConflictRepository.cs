using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDataConflictRepository"/> (FR-035, AC-4,
/// EP-008-II/us_044, task_001).
/// All queries use parameterised LINQ — no raw SQL (OWASP A03).
/// </summary>
public sealed class DataConflictRepository : IDataConflictRepository
{
    private readonly AppDbContext _context;

    public DataConflictRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DataConflict>> GetUnresolvedCriticalConflictsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.DataConflicts
            .Where(c =>
                c.PatientId == patientId &&
                c.Severity == DataConflictSeverity.Critical &&
                c.ResolutionStatus == DataConflictResolutionStatus.Unresolved)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> InsertIfNewAsync(
        DataConflict conflict,
        CancellationToken cancellationToken = default)
    {
        // Idempotency check: skip when an Unresolved record with the same patient/field/source
        // pair already exists (EP-008-II/us_044, task_001, AC-1 edge case — AIR-S02, OWASP A03).
        var exists = await _context.DataConflicts.AnyAsync(
            c => c.PatientId         == conflict.PatientId         &&
                 c.FieldName         == conflict.FieldName         &&
                 c.SourceDocumentId1 == conflict.SourceDocumentId1 &&
                 c.SourceDocumentId2 == conflict.SourceDocumentId2 &&
                 c.ResolutionStatus  == DataConflictResolutionStatus.Unresolved,
            cancellationToken);

        if (exists)
            return false;

        conflict.Id = Guid.NewGuid();
        _context.DataConflicts.Add(conflict);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DataConflict>> GetUnresolvedByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.DataConflicts
            .Where(c =>
                c.PatientId       == patientId &&
                c.ResolutionStatus == DataConflictResolutionStatus.Unresolved)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetCriticalUnresolvedCountAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.DataConflicts.CountAsync(
            c => c.PatientId        == patientId &&
                 c.Severity         == DataConflictSeverity.Critical &&
                 c.ResolutionStatus == DataConflictResolutionStatus.Unresolved,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DataConflict>> GetByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        // Include source documents so handlers can map FileName to the DTO
        // without issuing additional queries (OWASP A03 — parameterised LINQ only).
        return await _context.DataConflicts
            .Include(c => c.SourceDocument1)
            .Include(c => c.SourceDocument2)
            .Where(c => c.PatientId == patientId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DataConflict?> GetByIdAsync(
        Guid conflictId,
        CancellationToken cancellationToken = default)
    {
        // Entity is returned as tracked (no AsNoTracking) so the caller can mutate it
        // and persist via UpdateAsync within the same DbContext scope (AC-3).
        return await _context.DataConflicts
            .Include(c => c.SourceDocument1)
            .Include(c => c.SourceDocument2)
            .FirstOrDefaultAsync(c => c.Id == conflictId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        DataConflict conflict,
        CancellationToken cancellationToken = default)
    {
        // The entity was loaded as tracked by GetByIdAsync in the same scope;
        // EF Core change tracking detects the field mutations and issues a targeted UPDATE.
        await _context.SaveChangesAsync(cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IClinicalDocumentRepository"/> supporting the
/// AI extraction pipeline worker (US_040, task_004 — ExtractionPipelineWorker).
/// <para>
/// Security invariants:
/// <list type="bullet">
///   <item><description>OWASP A03: all queries use EF Core parameterized predicates — no raw SQL string interpolation.</description></item>
///   <item><description>AC-1: <c>GetPendingAsync</c> filters strictly by <c>ProcessingStatus = Pending</c> and applies FIFO ordering.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ClinicalDocumentRepository : IClinicalDocumentRepository
{
    private readonly AppDbContext _context;

    public ClinicalDocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClinicalDocument>> GetPendingAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        return await _context.ClinicalDocuments
            .Where(d => d.ProcessingStatus == DocumentProcessingStatus.Pending
                        && d.DeletedAt == null)
            .OrderBy(d => d.UploadedAt)
            .Take(batchSize)
            .Include(d => d.Patient)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        Guid documentId,
        DocumentProcessingStatus status,
        CancellationToken ct = default)
    {
        await _context.ClinicalDocuments
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(d => d.ProcessingStatus, status),
                ct);
    }
}

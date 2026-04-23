using MediatR;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Features.Documents.Dtos;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Features.Documents.GetClinicalDocuments;

/// <summary>
/// Handles <see cref="GetClinicalDocumentsQuery"/> for
/// <c>GET /api/documents</c> (US_038, AC-2, AD-2 CQRS read model).
/// <list type="number">
///   <item>Queries <c>clinical_documents</c> with <c>AsNoTracking()</c> — no entity tracking required (AD-2).</item>
///   <item>Filters by <c>patientId</c> (from JWT — OWASP A01) and excludes soft-deleted documents (<c>deletedAt IS NULL</c>).</item>
///   <item>Returns only <c>PatientUpload</c> source-type documents belonging to the authenticated patient.</item>
///   <item>Returns results ordered by <c>uploadedAt DESC</c>.</item>
/// </list>
/// </summary>
public sealed class GetClinicalDocumentsQueryHandler
    : IRequestHandler<GetClinicalDocumentsQuery, List<DocumentHistoryItemDto>>
{
    private readonly AppDbContext _db;

    public GetClinicalDocumentsQueryHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<DocumentHistoryItemDto>> Handle(
        GetClinicalDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        return await _db.ClinicalDocuments
            .AsNoTracking()
            .Where(d => d.PatientId == request.PatientId && d.DeletedAt == null)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentHistoryItemDto(
                d.Id,
                d.FileName,
                d.FileSize,
                d.SourceType.ToString(),
                // UploadedBy is always null for patient self-uploads (AC-2).
                null,
                d.EncounterReference,
                d.ProcessingStatus.ToString(),
                d.UploadedAt,
                // Patient self-uploaded documents are not deletable via this endpoint (US_038).
                false
            ))
            .ToListAsync(cancellationToken);
    }
}

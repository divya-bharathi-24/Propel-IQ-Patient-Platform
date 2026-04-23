using MediatR;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Features.Documents.Dtos;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Features.Documents.GetPatientDocuments;

/// <summary>
/// Handles <see cref="GetPatientDocumentsQuery"/> for
/// <c>GET /api/staff/patients/{patientId}/documents</c> (US_039, AC-2, AD-2 CQRS read model).
/// <list type="number">
///   <item>Queries <c>clinical_documents</c> with <c>AsNoTracking()</c> — no entity tracking required (AD-2).</item>
///   <item>Filters by <c>patientId</c> and excludes soft-deleted documents (<c>deletedAt IS NULL</c>).</item>
///   <item>Left-joins <c>users</c> to resolve <c>uploadedByName</c> for <c>StaffUpload</c> rows (AC-2).</item>
///   <item>Computes <c>isDeletable</c> inline: <c>sourceType == StaffUpload &amp;&amp; uploadedAt &gt;= UtcNow.AddHours(-24)</c>.</item>
///   <item>Returns results ordered by <c>uploadedAt DESC</c>.</item>
/// </list>
/// </summary>
public sealed class GetPatientDocumentsQueryHandler
    : IRequestHandler<GetPatientDocumentsQuery, List<DocumentHistoryItemDto>>
{
    private readonly AppDbContext _db;

    public GetPatientDocumentsQueryHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<DocumentHistoryItemDto>> Handle(
        GetPatientDocumentsQuery request,
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
                // Left-join: UploadedBy is null for PatientUpload rows (AC-2).
                d.UploadedBy != null ? d.UploadedBy.Name : null,
                d.EncounterReference,
                d.ProcessingStatus.ToString(),
                d.UploadedAt,
                // isDeletable: StaffUpload within 24h window and not yet soft-deleted.
                d.SourceType == DocumentSourceType.StaffUpload
                    && d.UploadedAt >= utcNow.AddHours(-24)
                    && d.DeletedAt == null
            ))
            .ToListAsync(cancellationToken);
    }
}

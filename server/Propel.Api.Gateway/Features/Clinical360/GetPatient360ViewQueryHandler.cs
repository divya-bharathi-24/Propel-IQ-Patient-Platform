using MediatR;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Enums;
using Propel.Modules.Clinical.Queries;

namespace Propel.Api.Gateway.Features.Clinical360;

/// <summary>
/// Handles <see cref="GetPatient360ViewQuery"/> for
/// <c>GET /api/staff/patients/{patientId}/360-view</c> (AC-1, AC-2).
///
/// <list type="bullet">
///   <item>Reads pre-aggregated <c>ExtractedData</c> written by the AI pipeline (task_003) — no inline AI calls (SLA gate).</item>
///   <item>Groups by <c>DataType</c> then <c>FieldName</c>; per field, the record with the highest confidence is the canonical entry.</item>
///   <item>Builds source citation arrays from all contributing records for each field.</item>
///   <item>Sets <c>IsLowConfidence = Confidence &lt; 0.80</c> (AIR-003).</item>
///   <item>Returns HTTP 202 (via <c>null</c> result) when no <c>ExtractedData</c> exists for the patient (aggregation in progress).</item>
///   <item>Sets <c>ExceedsSlaThreshold = true</c> when completed documents &gt; 10.</item>
///   <item>Enriches response with <c>PatientProfileVerification</c> when available.</item>
/// </list>
///
/// All EF Core queries use parameterised LINQ — no raw SQL (OWASP A03).
/// </summary>
public sealed class GetPatient360ViewQueryHandler
    : IRequestHandler<GetPatient360ViewQuery, Patient360ViewDto?>
{
    private const decimal LowConfidenceThreshold = 0.80m;
    private const int SlaDocumentLimit = 10;

    private readonly AppDbContext _db;

    public GetPatient360ViewQueryHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Patient360ViewDto?> Handle(
        GetPatient360ViewQuery request,
        CancellationToken cancellationToken)
    {
        // ── 1. Load all clinical documents for this patient (Completed + Failed) ──
        var documents = await _db.ClinicalDocuments
            .AsNoTracking()
            .Where(d => d.PatientId == request.PatientId && d.DeletedAt == null)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.ProcessingStatus,
                d.UploadedAt
            })
            .ToListAsync(cancellationToken);

        var completedDocumentIds = documents
            .Where(d => d.ProcessingStatus == DocumentProcessingStatus.Completed)
            .Select(d => d.Id)
            .ToHashSet();

        // ── 2. SLA gate: return null (HTTP 202) if no extracted data exists ─────
        var hasExtractedData = await _db.ExtractedData
            .AnyAsync(e => e.PatientId == request.PatientId, cancellationToken);

        if (!hasExtractedData)
            return null;

        // ── 3. Load extracted data for completed documents only ──────────────────
        var extractedData = await _db.ExtractedData
            .AsNoTracking()
            .Where(e =>
                e.PatientId == request.PatientId &&
                completedDocumentIds.Contains(e.DocumentId))
            .Join(
                _db.ClinicalDocuments.AsNoTracking(),
                e => e.DocumentId,
                d => d.Id,
                (e, d) => new
                {
                    e.Id,
                    e.DataType,
                    e.FieldName,
                    e.Value,
                    e.Confidence,
                    e.SourcePageNumber,
                    e.DocumentId,
                    d.FileName,
                    d.UploadedAt
                })
            .ToListAsync(cancellationToken);

        // ── 4. Group by DataType → FieldName; canonical = highest confidence ─────
        var sections = extractedData
            .GroupBy(e => e.DataType)
            .Select(dataTypeGroup =>
            {
                var items = dataTypeGroup
                    .GroupBy(e => e.FieldName)
                    .Select(fieldGroup =>
                    {
                        var canonical = fieldGroup
                            .OrderByDescending(e => e.Confidence)
                            .First();

                        var sources = fieldGroup
                            .Select(e => new SourceCitationDto(
                                e.FileName,
                                e.SourcePageNumber,
                                e.UploadedAt))
                            .ToList();

                        return new ClinicalItemDto(
                            canonical.FieldName,
                            canonical.Value,
                            canonical.Confidence,
                            canonical.Confidence < LowConfidenceThreshold,
                            sources);
                    })
                    .ToList();

                return new ClinicalSectionDto(
                    dataTypeGroup.Key.ToString(),
                    items);
            })
            .ToList();

        // ── 5. Build document status list ────────────────────────────────────────
        var documentStatuses = documents
            .Select(d => new DocumentStatusDto(
                d.Id,
                d.FileName,
                d.ProcessingStatus.ToString(),
                d.UploadedAt))
            .ToList();

        // ── 6. SLA threshold flag ────────────────────────────────────────────────
        var exceedsSlaThreshold = completedDocumentIds.Count > SlaDocumentLimit;

        // ── 7. Enrich with PatientProfileVerification (if exists) ────────────────
        var verification = await _db.PatientProfileVerifications
            .AsNoTracking()
            .Where(v => v.PatientId == request.PatientId)
            .Join(
                _db.Users.AsNoTracking(),
                v => v.VerifiedBy,
                u => u.Id,
                (v, u) => new VerificationInfoDto(
                    v.Status.ToString(),
                    v.VerifiedAt,
                    u.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return new Patient360ViewDto(
            request.PatientId,
            sections,
            documentStatuses,
            exceedsSlaThreshold,
            verification);
    }
}

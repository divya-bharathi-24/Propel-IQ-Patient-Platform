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
///   <item>Enriches response with conflict data and PatientProfileVerification when available.</item>
/// </list>
///
/// Response shape matches the Angular <c>Patient360ViewDto</c> interface (US_041, task_001).
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

        // ── 2. SLA gate: return null (HTTP 202) while pipeline is still running ─
        // Return 202 only when at least one document is still Pending or Processing.
        // If all documents are in terminal states (Completed / Failed) — including the
        // chunking-only case where the AI key is absent and no ExtractedData rows were
        // written — fall through and build the response with whatever data exists.
        var hasPipelineInProgress = documents.Any(d =>
            d.ProcessingStatus == DocumentProcessingStatus.Pending ||
            d.ProcessingStatus == DocumentProcessingStatus.Processing);

        if (hasPipelineInProgress)
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

                // SectionType matches the Angular SectionType union (task_001, AC-1)
                return new ClinicalSectionDto(
                    dataTypeGroup.Key.ToString(),
                    items);
            })
            .ToList();

        // ── 5. Build document status list ────────────────────────────────────────
        var documentStatuses = documents
            .Select(d => new DocumentStatusDto(
                d.Id,
                d.FileName,     // serialised as "documentName" (camelCase)
                d.ProcessingStatus.ToString(),
                d.UploadedAt))
            .ToList();

        // ── 6. Load conflicts for this patient (US_044; empty when not yet detected) ─
        var conflictEntities = await _db.DataConflicts
            .AsNoTracking()
            .Include(c => c.SourceDocument1)
            .Include(c => c.SourceDocument2)
            .Where(c => c.PatientId == request.PatientId)
            .ToListAsync(cancellationToken);

        var conflicts = conflictEntities
            .Select(c => new DataConflictItemDto(
                c.Id,
                c.FieldName,
                c.Severity.ToString(),
                c.ResolutionStatus.ToString(),
                c.Value1,
                c.SourceDocument1?.FileName ?? string.Empty,
                c.Value2,
                c.SourceDocument2?.FileName ?? string.Empty,
                c.ResolvedValue))
            .ToList();

        var unresolvedCritical = conflictEntities
            .Where(c =>
                c.Severity == DataConflictSeverity.Critical &&
                c.ResolutionStatus == DataConflictResolutionStatus.Unresolved)
            .Select(c => new ConflictSummaryDto(
                c.FieldName,
                $"Conflicting values across source documents"))
            .ToList();

        // ── 7. Enrich with PatientProfileVerification (if exists) ────────────────
        var verificationRow = await _db.PatientProfileVerifications
            .AsNoTracking()
            .Where(v => v.PatientId == request.PatientId)
            .Join(
                _db.Users.AsNoTracking(),
                v => v.VerifiedBy,
                u => u.Id,
                (v, u) => new
                {
                    Status = v.Status.ToString(),
                    v.VerifiedAt,
                    VerifiedByName = u.Name
                })
            .FirstOrDefaultAsync(cancellationToken);

        // ── 8. Load patient demographics (PHI auto-decrypted via EF value converters) ────
        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken);

        var dobString = patient != null && patient.DateOfBirth != default(DateOnly)
            ? patient.DateOfBirth.ToString("yyyy-MM-dd")
            : null;

        // ── 9. Load latest appointment for risk level and visit type ─────────────
        var latestAppt = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.NoShowRisk)
            .Include(a => a.QueueEntry)
            .Include(a => a.Specialty)
            .Where(a => a.PatientId == request.PatientId)
            .OrderByDescending(a => a.Date)
            .FirstOrDefaultAsync(cancellationToken);

        var visitType = latestAppt?.QueueEntry != null ? "Walk-in" : "Scheduled";
        var riskLevel = latestAppt?.NoShowRisk?.Severity;

        // ── 10. Compute last-updated timestamp from most recent document upload ──
        var lastUpdatedAt = documents.Any()
            ? documents.Max(d => (DateTime?)d.UploadedAt)
            : null;

        // ── 11. Load latest completed intake record for this patient ─────────────
        var latestIntake = await _db.IntakeRecords
            .AsNoTracking()
            .Where(r => r.PatientId == request.PatientId && r.CompletedAt != null)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var intakeSnapshot = latestIntake is not null
            ? new IntakeSnapshotDto(
                latestIntake.Demographics,
                latestIntake.MedicalHistory,
                latestIntake.Symptoms,
                latestIntake.Medications,
                "Submitted",
                latestIntake.CompletedAt)
            : null;

        return new Patient360ViewDto(
            request.PatientId,
            patient?.Name,
            dobString,
            patient?.InsurerName,
            visitType,
            riskLevel,
            lastUpdatedAt,
            verificationRow?.Status ?? "Unverified",
            verificationRow?.VerifiedAt,
            verificationRow?.VerifiedByName,
            unresolvedCritical,
            conflicts,
            documentStatuses,
            sections,
            intakeSnapshot);
    }
}

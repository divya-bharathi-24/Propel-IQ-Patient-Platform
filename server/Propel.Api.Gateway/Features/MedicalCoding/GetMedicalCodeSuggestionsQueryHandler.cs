using MediatR;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Dtos;
using Propel.Domain.Enums;
using Propel.Modules.AI.Interfaces;

namespace Propel.Api.Gateway.Features.MedicalCoding;

/// <summary>
/// Handles <see cref="GetMedicalCodeSuggestionsQuery"/> for
/// <c>GET /api/patients/{patientId}/medical-codes</c> (EP-008-II/us_042, task_002, AC-1–AC-4).
///
/// <list type="bullet">
///   <item>Loads completed clinical documents and canonical extracted data from the DB.</item>
///   <item>Guards on empty <c>SourceDocumentIds</c>: returns HTTP 200 with empty suggestion list and explanatory message (EC-1).</item>
///   <item>Builds <see cref="AggregatedPatientData"/> from <c>ExtractedData</c> and <c>PatientProfileVerification</c> records.</item>
///   <item>Delegates to <see cref="IMedicalCodingOrchestrator.SuggestCodesAsync"/> — no inline AI calls in this class (SLA gate).</item>
///   <item>If <c>MedicalCodingUnavailableException</c> is thrown (circuit breaker open), lets it propagate to <c>ExceptionHandlingMiddleware</c> → HTTP 503 (EC-2).</item>
///   <item>Maps <see cref="MedicalCodingSuggestionResult"/> to <see cref="MedicalCodeSuggestionsResponse"/> and returns.</item>
/// </list>
///
/// All EF Core queries use parameterised LINQ — no raw SQL (OWASP A03).
/// PatientId is sourced from the validated JWT claim in the controller; never from user-supplied body (OWASP A01).
/// </summary>
public sealed class GetMedicalCodeSuggestionsQueryHandler
    : IRequestHandler<GetMedicalCodeSuggestionsQuery, MedicalCodeSuggestionsResponse>
{
    private const string NoClinicalDataMessage =
        "No clinical data available for code analysis — upload documents first";

    private readonly AppDbContext _db;
    private readonly IMedicalCodingOrchestrator _orchestrator;

    public GetMedicalCodeSuggestionsQueryHandler(
        AppDbContext db,
        IMedicalCodingOrchestrator orchestrator)
    {
        _db = db;
        _orchestrator = orchestrator;
    }

    public async Task<MedicalCodeSuggestionsResponse> Handle(
        GetMedicalCodeSuggestionsQuery request,
        CancellationToken cancellationToken)
    {
        // ── 1. Load completed clinical document IDs for the patient ────────────
        var completedDocumentIds = await _db.ClinicalDocuments
            .AsNoTracking()
            .Where(d =>
                d.PatientId == request.PatientId &&
                d.DeletedAt == null &&
                d.ProcessingStatus == DocumentProcessingStatus.Completed)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        // ── 2. EC-1 guard: no completed documents → return empty result immediately ──
        if (completedDocumentIds.Count == 0)
        {
            return new MedicalCodeSuggestionsResponse
            {
                Suggestions = [],
                Message = NoClinicalDataMessage
            };
        }

        // ── 3. Load canonical extracted data for the completed documents ────────
        // Canonical records are set by the AI de-duplication pipeline (us_041/task_003, AIR-002).
        // Non-canonical duplicates are excluded to avoid inflating the prompt context.
        var extractedRows = await _db.ExtractedData
            .AsNoTracking()
            .Where(e =>
                e.PatientId == request.PatientId &&
                completedDocumentIds.Contains(e.DocumentId) &&
                e.IsCanonical)
            .Select(e => new { e.DataType, e.FieldName, e.Value })
            .ToListAsync(cancellationToken);

        // ── 4. Build diagnostic summary (Diagnosis, Vital, Allergy, History) ────
        // Formatted as "FieldName: Value" key-value pairs, one per line (AIR-O01).
        var diagnosticTypes = new[]
        {
            ExtractedDataType.Diagnosis,
            ExtractedDataType.Vital,
            ExtractedDataType.Allergy,
            ExtractedDataType.History
        };

        var diagnosticSummary = string.Join(
            Environment.NewLine,
            extractedRows
                .Where(e => diagnosticTypes.Contains(e.DataType))
                .Select(e => $"{e.FieldName}: {e.Value}"));

        // ── 5. Build procedure summary (Medication, History) ─────────────────────
        var procedureTypes = new[]
        {
            ExtractedDataType.Medication,
            ExtractedDataType.History
        };

        var procedureSummary = string.Join(
            Environment.NewLine,
            extractedRows
                .Where(e => procedureTypes.Contains(e.DataType))
                .Select(e => $"{e.FieldName}: {e.Value}"));

        // ── 6. Resolve verification status (default: Pending when no record exists) ──
        var verificationStatus = await _db.PatientProfileVerifications
            .AsNoTracking()
            .Where(v => v.PatientId == request.PatientId)
            .Select(v => v.Status.ToString())
            .FirstOrDefaultAsync(cancellationToken)
            ?? VerificationStatus.Pending.ToString();

        // ── 7. Assemble AggregatedPatientData for the orchestrator ───────────────
        var aggregatedData = new AggregatedPatientData
        {
            PatientId = request.PatientId,
            VerificationStatus = verificationStatus,
            DiagnosticSummary = diagnosticSummary,
            ProcedureSummary = procedureSummary,
            SourceDocumentIds = completedDocumentIds.AsReadOnly()
        };

        // ── 8. Invoke the AI coding pipeline ────────────────────────────────────
        // MedicalCodingUnavailableException (circuit breaker open — EC-2) is NOT caught here;
        // ExceptionHandlingMiddleware maps it to HTTP 503 with a structured error body.
        var result = await _orchestrator.SuggestCodesAsync(aggregatedData, cancellationToken);

        // ── 9. Map orchestrator result → API response ─────────────────────────────
        return new MedicalCodeSuggestionsResponse
        {
            Suggestions = result.Suggestions,
            Message = result.Message
        };
    }
}

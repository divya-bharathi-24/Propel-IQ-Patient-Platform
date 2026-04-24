using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Validators;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IMedicalCodingOrchestrator"/> that runs the
/// sequential ICD-10 → CPT Semantic Kernel tool-calling pipeline (EP-008-II/us_042, task_001).
/// <para>
/// Pipeline steps per <c>SuggestCodesAsync</c> call:
/// <list type="number">
///   <item><description>Guard empty-data edge case: return empty result with message when no source documents exist (EC-1).</description></item>
///   <item><description>Check Polly circuit breaker — throw <see cref="MedicalCodingUnavailableException"/> if open (EC-2, AIR-O02).</description></item>
///   <item><description>Call <c>SuggestIcd10CodesAsync</c> via <see cref="MedicalCodingPlugin"/> (AC-1).</description></item>
///   <item><description>Validate ICD-10 output via <see cref="MedicalCodeSchemaValidator"/> (AIR-Q03).</description></item>
///   <item><description>Append ICD-10 code context to procedure summary, then call <c>SuggestCptCodesAsync</c> (AC-2).</description></item>
///   <item><description>Validate CPT output via <see cref="MedicalCodeSchemaValidator"/> (AIR-Q03).</description></item>
///   <item><description>Merge ICD-10 + CPT lists; set <c>lowConfidence = true</c> for codes with confidence &lt; 0.80 (AC-4, AIR-003).</description></item>
///   <item><description>Write audit log entry — no PHI in log body (AIR-S03).</description></item>
/// </list>
/// </para>
/// <para>
/// Circuit breaker behaviour (AIR-O02): each tool call is wrapped individually. The first
/// failure triggers a retry on the same call. A second failure within the sampling window
/// throws <see cref="MedicalCodingUnavailableException"/> so the BE layer can trigger the
/// manual-entry fallback notification.
/// </para>
/// </summary>
public sealed class MedicalCodingOrchestrator : IMedicalCodingOrchestrator
{
    private const decimal LowConfidenceThreshold = 0.80m;

    // ── Max chars appended as ICD-10 context prefix in the CPT procedure summary ─
    // Keeps the combined CPT prompt well within the 7,200-token context budget (AIR-O01).
    private const int MaxIcd10ContextChars = 2_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MedicalCodingPlugin       _plugin;
    private readonly MedicalCodeSchemaValidator _validator;
    private readonly IAuditLogRepository        _auditLog;
    private readonly ResiliencePipeline         _circuitBreaker;

    public MedicalCodingOrchestrator(
        MedicalCodingPlugin plugin,
        MedicalCodeSchemaValidator validator,
        IAuditLogRepository auditLog,
        [FromKeyedServices("medical-coding")] ResiliencePipeline circuitBreaker)
    {
        _plugin         = plugin;
        _validator      = validator;
        _auditLog       = auditLog;
        _circuitBreaker = circuitBreaker;
    }

    /// <inheritdoc/>
    public async Task<MedicalCodingSuggestionResult> SuggestCodesAsync(
        AggregatedPatientData patientData,
        CancellationToken ct = default)
    {
        // ── Step 1: Empty-data guard (EC-1) ───────────────────────────────────
        if (patientData.SourceDocumentIds.Count == 0 ||
            (string.IsNullOrWhiteSpace(patientData.DiagnosticSummary) &&
             string.IsNullOrWhiteSpace(patientData.ProcedureSummary)))
        {
            Log.Warning(
                "MedicalCodingOrchestrator_NoClinicalData: patientId={PatientId} — " +
                "no source documents or summaries available.",
                patientData.PatientId);

            return new MedicalCodingSuggestionResult
            {
                Suggestions = [],
                Message     = "No clinical data available for code analysis — upload documents first"
            };
        }

        // ── Step 2: Pre-call circuit breaker check (EC-2, AIR-O02) ───────────
        if (IsCircuitOpen())
        {
            Log.Warning(
                "MedicalCodingOrchestrator_CircuitBreakerOpen: patientId={PatientId} — " +
                "medical coding unavailable; circuit is open (AIR-O02).",
                patientData.PatientId);

            throw new MedicalCodingUnavailableException(
                "Medical coding AI service is temporarily unavailable — circuit breaker is open. " +
                "Please use manual code entry.");
        }

        var primarySourceDocumentId = patientData.SourceDocumentIds.Count > 0
            ? patientData.SourceDocumentIds[0]
            : Guid.Empty;

        // ── Step 3: ICD-10 tool call (AC-1) ──────────────────────────────────
        var stopwatchIcd10 = Stopwatch.StartNew();
        List<Domain.Dtos.MedicalCodeSuggestionDto> rawIcd10;

        try
        {
            rawIcd10 = await _circuitBreaker.ExecuteAsync(
                async ct2 => await _plugin.SuggestIcd10CodesAsync(
                    patientData.DiagnosticSummary,
                    patientData.PatientId,
                    primarySourceDocumentId,
                    ct2),
                ct);
        }
        catch (BrokenCircuitException ex)
        {
            Log.Warning(
                "MedicalCodingOrchestrator_CircuitBreakerOpen_ICD10: patientId={PatientId} {Message}",
                patientData.PatientId, ex.Message);
            throw new MedicalCodingUnavailableException(
                "Medical coding AI service is temporarily unavailable after repeated failures (ICD-10 phase).", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex,
                "MedicalCodingOrchestrator_Icd10CallFailed: patientId={PatientId}",
                patientData.PatientId);
            throw new MedicalCodingUnavailableException(
                $"ICD-10 code suggestion failed: {ex.Message}", ex);
        }

        stopwatchIcd10.Stop();

        // ── Step 4: Validate ICD-10 output (AIR-Q03, AC-3) ───────────────────
        var validatedIcd10 = _validator.Validate(rawIcd10, "ICD10");

        // ── Step 5: Enrich CPT context with ICD-10 evidence and call CPT tool (AC-2) ─
        // Prepend a compact ICD-10 summary so GPT-4o can map diagnosis context to procedure codes.
        var icd10Context = BuildIcd10ContextPrefix(validatedIcd10);
        var enrichedProcedureData = string.IsNullOrWhiteSpace(icd10Context)
            ? patientData.ProcedureSummary
            : $"ICD-10 Diagnoses (context):\n{icd10Context}\n\nProcedures:\n{patientData.ProcedureSummary}";

        var stopwatchCpt = Stopwatch.StartNew();
        List<Domain.Dtos.MedicalCodeSuggestionDto> rawCpt;

        try
        {
            rawCpt = await _circuitBreaker.ExecuteAsync(
                async ct2 => await _plugin.SuggestCptCodesAsync(
                    enrichedProcedureData,
                    patientData.PatientId,
                    primarySourceDocumentId,
                    ct2),
                ct);
        }
        catch (BrokenCircuitException ex)
        {
            Log.Warning(
                "MedicalCodingOrchestrator_CircuitBreakerOpen_CPT: patientId={PatientId} {Message}",
                patientData.PatientId, ex.Message);
            throw new MedicalCodingUnavailableException(
                "Medical coding AI service is temporarily unavailable after repeated failures (CPT phase).", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex,
                "MedicalCodingOrchestrator_CptCallFailed: patientId={PatientId}",
                patientData.PatientId);
            throw new MedicalCodingUnavailableException(
                $"CPT code suggestion failed: {ex.Message}", ex);
        }

        stopwatchCpt.Stop();

        // ── Step 6: Validate CPT output (AIR-Q03, AC-3) ──────────────────────
        var validatedCpt = _validator.Validate(rawCpt, "CPT");

        // ── Step 7: Merge and set low-confidence flags (AC-4, AIR-003) ────────
        var merged = validatedIcd10
            .Concat(validatedCpt)
            .Select(s => s with { LowConfidence = s.Confidence < LowConfidenceThreshold })
            .OrderByDescending(s => s.Confidence)
            .ToList();

        int lowConfidenceCount = merged.Count(s => s.LowConfidence);

        // ── Step 8: Audit log — patient ID reference only, no PHI (AIR-S03) ──
        long totalLatencyMs = stopwatchIcd10.ElapsedMilliseconds + stopwatchCpt.ElapsedMilliseconds;
        await WriteAuditLogAsync(
            patientData.PatientId,
            icd10Count        : validatedIcd10.Count,
            cptCount          : validatedCpt.Count,
            lowConfidenceCount: lowConfidenceCount,
            totalLatencyMs    : totalLatencyMs,
            schemaValid       : rawIcd10.Count == validatedIcd10.Count && rawCpt.Count == validatedCpt.Count,
            ct);

        Log.Information(
            "MedicalCodingOrchestrator_Completed: patientId={PatientId} " +
            "icd10={Icd10Count} cpt={CptCount} lowConfidence={LowConfidenceCount} latencyMs={LatencyMs}",
            patientData.PatientId, validatedIcd10.Count, validatedCpt.Count, lowConfidenceCount, totalLatencyMs);

        return new MedicalCodingSuggestionResult
        {
            Suggestions = merged
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Probes the Polly circuit breaker with a no-op to detect open state before making an API call.
    /// Returns <c>true</c> when the circuit is open (AIR-O02).
    /// </summary>
    private bool IsCircuitOpen()
    {
        try
        {
            _circuitBreaker.Execute(() => { });
            return false;
        }
        catch (BrokenCircuitException)
        {
            return true;
        }
    }

    /// <summary>
    /// Builds a compact ICD-10 code summary string (max <see cref="MaxIcd10ContextChars"/> chars)
    /// to prepend to the CPT procedure summary as diagnostic context.
    /// </summary>
    private static string BuildIcd10ContextPrefix(
        IReadOnlyList<Domain.Dtos.MedicalCodeSuggestionDto> icd10Codes)
    {
        if (icd10Codes.Count == 0) return string.Empty;

        var lines = icd10Codes
            .Where(c => c.Confidence >= 0.50m) // include moderate-confidence diagnoses as CPT context
            .Select(c => $"{c.Code}: {c.Description} (confidence: {c.Confidence:F2})")
            .ToList();

        var joined = string.Join("\n", lines);
        return joined.Length > MaxIcd10ContextChars ? joined[..MaxIcd10ContextChars] : joined;
    }

    /// <summary>
    /// Writes a structured audit log entry for the medical coding invocation.
    /// No patient PHI is included in the log body (AIR-S01, AIR-S03).
    /// </summary>
    private async Task WriteAuditLogAsync(
        Guid  patientId,
        int   icd10Count,
        int   cptCount,
        int   lowConfidenceCount,
        long  totalLatencyMs,
        bool  schemaValid,
        CancellationToken ct)
    {
        try
        {
            await _auditLog.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                PatientId  = patientId,
                Action     = "MedicalCodingCompleted",
                EntityType = "Patient",
                EntityId   = patientId,
                Details    = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    toolName          = "ICD10+CPT",
                    icd10Count,
                    cptCount,
                    suggestionCount   = icd10Count + cptCount,
                    lowConfidenceCount,
                    schemaValid,
                    latencyMs         = totalLatencyMs
                }, JsonOptions)),
                Timestamp  = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Audit log failure must never fail the primary pipeline (HIPAA 7-year retention is
            // handled by the infrastructure-level audit trigger — this is a best-effort application log).
            Log.Error(ex,
                "MedicalCodingOrchestrator_AuditLogFailed: patientId={PatientId}",
                patientId);
        }
    }
}

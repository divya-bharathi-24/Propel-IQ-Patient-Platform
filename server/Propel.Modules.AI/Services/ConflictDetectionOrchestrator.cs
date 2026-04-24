using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Validators;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IConflictDetectionOrchestrator"/> that runs the
/// Semantic Kernel RAG-based conflict detection pipeline over a patient's canonical extracted data
/// (EP-008-II/us_044, task_001, AC-1).
/// <para>
/// Pipeline steps per <c>DetectConflictsAsync</c> call:
/// <list type="number">
///   <item><description>Load canonical <see cref="ExtractedData"/> for the patient from completed documents (AIR-S02).</description></item>
///   <item><description>Group records by field name; discard groups with fewer than two distinct source documents (no pair to compare).</description></item>
///   <item><description>Check Polly circuit breaker — throw <see cref="ConflictDetectionUnavailableException"/> if open (AIR-O02).</description></item>
///   <item><description>For each field type with ≥ 2 distinct values from different source documents: invoke <c>DetectConflictsAsync</c> on each value pair via <see cref="ConflictDetectionPlugin"/>.</description></item>
///   <item><description>Validate AI output schema via <see cref="ConflictDetectionSchemaValidator"/> (AIR-Q03). Skip field pairs that fail validation; log at WARNING (AIR-003).</description></item>
///   <item><description>Apply <see cref="ConflictSeverityClassifier"/> to assign <c>Critical</c> / <c>Warning</c> severity (AC-1).</description></item>
///   <item><description>Call <see cref="IDataConflictRepository.InsertIfNewAsync"/> for each detected conflict (idempotent — skips existing Unresolved records).</description></item>
///   <item><description>Write structured audit log entry — no PHI in log body (AIR-S03).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ConflictDetectionOrchestrator : IConflictDetectionOrchestrator
{
    private const decimal LowConfidenceThreshold = 0.80m;

    private readonly ConflictDetectionPlugin      _plugin;
    private readonly ConflictDetectionSchemaValidator _validator;
    private readonly IExtractedDataRepository     _extractedDataRepo;
    private readonly IDataConflictRepository      _conflictRepo;
    private readonly IAuditLogRepository          _auditLog;
    private readonly ResiliencePipeline           _circuitBreaker;

    public ConflictDetectionOrchestrator(
        ConflictDetectionPlugin plugin,
        ConflictDetectionSchemaValidator validator,
        IExtractedDataRepository extractedDataRepo,
        IDataConflictRepository conflictRepo,
        IAuditLogRepository auditLog,
        [FromKeyedServices("conflict-detection")] ResiliencePipeline circuitBreaker)
    {
        _plugin            = plugin;
        _validator         = validator;
        _extractedDataRepo = extractedDataRepo;
        _conflictRepo      = conflictRepo;
        _auditLog          = auditLog;
        _circuitBreaker    = circuitBreaker;
    }

    /// <inheritdoc/>
    public async Task<int> DetectConflictsAsync(Guid patientId, CancellationToken ct = default)
    {
        // ── Step 1: Load canonical extraction records for patient (AIR-S02) ──
        var allFields = await _extractedDataRepo.GetCompletedByPatientIdAsync(patientId, ct);

        if (allFields.Count == 0)
        {
            Log.Information(
                "ConflictDetectionOrchestrator_NoFields: patientId={PatientId} — " +
                "no completed extraction records; skipping conflict detection.",
                patientId);
            return 0;
        }

        // ── Step 2: Group by field name — only canonical records from different docs ─
        // ACL filter: only records from the patient's own authorized documents (AIR-S02).
        var fieldGroups = allFields
            .Where(f => f.IsCanonical)
            .GroupBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(f => f.DocumentId).Distinct().Count() >= 2)
            .ToList();

        if (fieldGroups.Count == 0)
        {
            Log.Information(
                "ConflictDetectionOrchestrator_NoPairs: patientId={PatientId} — " +
                "no field groups with ≥2 distinct source documents; no conflicts to detect.",
                patientId);
            return 0;
        }

        // ── Step 3: Pre-call circuit breaker check (AIR-O02) ─────────────────
        if (IsCircuitOpen())
        {
            Log.Warning(
                "ConflictDetectionOrchestrator_CircuitBreakerOpen: patientId={PatientId} — " +
                "conflict detection unavailable; circuit is open (AIR-O02).",
                patientId);

            throw new ConflictDetectionUnavailableException(
                "Conflict detection AI service is temporarily unavailable — circuit breaker is open. " +
                "Please route to manual staff review.");
        }

        // ── Steps 4–7: Pair comparison, schema validation, severity, persist ─
        int newConflictsInserted = 0;
        int schemaFailures       = 0;
        var stopwatch            = Stopwatch.StartNew();

        foreach (var group in fieldGroups)
        {
            var fieldsInGroup = group.ToList();

            // Generate all distinct cross-document value pairs for the field.
            for (int i = 0; i < fieldsInGroup.Count - 1; i++)
            {
                for (int j = i + 1; j < fieldsInGroup.Count; j++)
                {
                    var field1 = fieldsInGroup[i];
                    var field2 = fieldsInGroup[j];

                    if (field1.DocumentId == field2.DocumentId)
                        continue;

                    var inserted = await DetectAndPersistPairAsync(
                        patientId, field1, field2, ct);

                    if (inserted is null)
                    {
                        schemaFailures++;
                    }
                    else if (inserted == true)
                    {
                        newConflictsInserted++;
                    }

                    // Track low-confidence invocations for audit telemetry.
                    // (Low confidence routing is handled inside DetectAndPersistPairAsync.)
                }
            }
        }

        stopwatch.Stop();

        // ── Step 8: Audit log — no PHI (AIR-S01, AIR-S03) ────────────────────
        await WriteAuditLogAsync(
            patientId,
            newConflictsInserted,
            schemaFailures,
            stopwatch.ElapsedMilliseconds,
            ct);

        Log.Information(
            "ConflictDetectionOrchestrator_Completed: patientId={PatientId} " +
            "newConflicts={NewConflicts} schemaFailures={SchemaFailures} latencyMs={LatencyMs}",
            patientId, newConflictsInserted, schemaFailures, stopwatch.ElapsedMilliseconds);

        return newConflictsInserted;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Compares a single field-value pair via the AI plugin, validates, classifies, and
    /// persists the result. Returns:
    /// <list type="bullet">
    ///   <item><description><c>true</c> — new conflict record inserted.</description></item>
    ///   <item><description><c>false</c> — no conflict detected or existing record skipped.</description></item>
    ///   <item><description><c>null</c> — schema validation failed; pair skipped.</description></item>
    /// </list>
    /// </summary>
    private async Task<bool?> DetectAndPersistPairAsync(
        Guid patientId,
        ExtractedData field1,
        ExtractedData field2,
        CancellationToken ct)
    {
        Dtos.ConflictDetectionResult rawResult;

        try
        {
            rawResult = await _circuitBreaker.ExecuteAsync(
                async ct2 => await _plugin.DetectConflictsAsync(
                    field1.FieldName,
                    field1.Value,
                    $"Document-{field1.DocumentId:N[..8]}",
                    field2.Value,
                    $"Document-{field2.DocumentId:N[..8]}",
                    patientId,
                    ct2),
                ct);
        }
        catch (BrokenCircuitException ex)
        {
            Log.Warning(
                "ConflictDetectionOrchestrator_CircuitBreakerOpen_Pair: patientId={PatientId} " +
                "fieldName={FieldName} {Message}",
                patientId, field1.FieldName, ex.Message);
            throw new ConflictDetectionUnavailableException(
                "Conflict detection AI service is temporarily unavailable after repeated failures.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex,
                "ConflictDetectionOrchestrator_PairCallFailed: patientId={PatientId} fieldName={FieldName}",
                patientId, field1.FieldName);
            return null;
        }

        // Enrich raw result with source document IDs (plugin sets these to Guid.Empty).
        rawResult = rawResult with
        {
            SourceDocumentId1 = field1.DocumentId,
            SourceDocumentId2 = field2.DocumentId
        };

        // ── Schema validation (AIR-Q03) ────────────────────────────────────────
        if (!_validator.TryValidate(rawResult, field1.FieldName))
            return null;

        // ── Low-confidence fallback (AIR-003): log for manual review, continue ─
        if (rawResult.Confidence < LowConfidenceThreshold)
        {
            Log.Warning(
                "ConflictDetectionOrchestrator_LowConfidence: patientId={PatientId} " +
                "fieldName={FieldName} confidence={Confidence} — flagged for manual staff review (AIR-003).",
                patientId, field1.FieldName, rawResult.Confidence);
        }

        if (!rawResult.IsConflict)
            return false;

        // ── Severity classification (AC-1) ─────────────────────────────────────
        var severity = ConflictSeverityClassifier.Classify(field1.FieldName);

        var conflict = new DataConflict
        {
            PatientId         = patientId,
            FieldName         = rawResult.FieldName,
            Value1            = rawResult.Value1,
            SourceDocumentId1 = rawResult.SourceDocumentId1,
            Value2            = rawResult.Value2,
            SourceDocumentId2 = rawResult.SourceDocumentId2,
            ResolutionStatus  = DataConflictResolutionStatus.Unresolved,
            Severity          = severity
        };

        // ── Idempotent insert (AC-1 edge case) ────────────────────────────────
        var inserted = await _conflictRepo.InsertIfNewAsync(conflict, ct);
        return inserted;
    }

    /// <summary>
    /// Probes the Polly circuit breaker with a no-op to detect open state (AIR-O02).
    /// Returns <c>true</c> when the circuit is open.
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
    /// Writes a structured audit log entry after the conflict detection run.
    /// No patient PHI is included — only patient ID reference, counts, and latency (AIR-S01, AIR-S03).
    /// </summary>
    private async Task WriteAuditLogAsync(
        Guid patientId,
        int newConflictsInserted,
        int schemaFailures,
        long latencyMs,
        CancellationToken ct)
    {
        try
        {
            await _auditLog.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                PatientId  = patientId,
                Action     = "ConflictDetectionCompleted",
                EntityType = "Patient",
                EntityId   = patientId,
                Details    = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    toolName             = "ConflictDetection",
                    newConflictsInserted,
                    schemaFailures,
                    latencyMs,
                    timestamp            = DateTime.UtcNow
                    // No PHI: no patient names, no extracted field values (AIR-S03)
                })),
                Timestamp = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "ConflictDetectionOrchestrator_AuditLogFailed: patientId={PatientId}",
                patientId);
        }
    }
}

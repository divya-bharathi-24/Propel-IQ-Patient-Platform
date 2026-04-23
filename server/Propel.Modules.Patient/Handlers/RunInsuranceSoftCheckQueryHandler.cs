using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Queries;
using Propel.Modules.Patient.Services;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="RunInsuranceSoftCheckQuery"/> for <c>POST /api/insurance/pre-check</c>
/// (US_022, task_002, AC-1, AC-2, AC-3, AC-4, NFR-013, NFR-018).
/// <list type="number">
///   <item><b>Step 1 — Classify</b>: calls <see cref="InsuranceSoftCheckClassifier.Classify"/>
///         to detect incomplete fields (no DB call). Returns immediately on <c>Incomplete</c>.</item>
///   <item><b>Step 2 — DB lookup</b>: queries <see cref="IDummyInsurersRepository.ExistsAsync"/>
///         inside a try/catch. Any exception → <c>CheckPending</c> (NFR-018, FR-040).</item>
///   <item><b>Step 3 — Map result</b>: maps boolean match result to <c>Verified</c> or
///         <c>NotRecognized</c> with the appropriate guidance constant.</item>
/// </list>
/// <para>
/// No <c>InsuranceValidation</c> record is written here. Record creation happens at booking
/// confirmation time inside <c>POST /api/appointments/book</c> (US_019, task_002).
/// This handler is a read-only classification query (AC-3 — does not persist).
/// </para>
/// <para>
/// PHI (providerName / insuranceId) is NEVER logged in plain text (NFR-013, HIPAA).
/// Correlation logging uses only <c>patientId</c>, <c>status</c>, and <c>durationMs</c>.
/// </para>
/// </summary>
public sealed class RunInsuranceSoftCheckQueryHandler
    : IRequestHandler<RunInsuranceSoftCheckQuery, InsurancePreCheckResult>
{
    private readonly InsuranceSoftCheckClassifier _classifier;
    private readonly IDummyInsurersRepository _dummyInsurers;
    private readonly ILogger<RunInsuranceSoftCheckQueryHandler> _logger;

    public RunInsuranceSoftCheckQueryHandler(
        InsuranceSoftCheckClassifier classifier,
        IDummyInsurersRepository dummyInsurers,
        ILogger<RunInsuranceSoftCheckQueryHandler> logger)
    {
        _classifier    = classifier;
        _dummyInsurers = dummyInsurers;
        _logger        = logger;
    }

    public async Task<InsurancePreCheckResult> Handle(
        RunInsuranceSoftCheckQuery request,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;

        // Step 1 — Classify: detect missing fields without hitting the database.
        var classified = _classifier.Classify(request.ProviderName, request.InsuranceId);
        if (classified.Status == InsuranceValidationResult.Incomplete)
        {
            _logger.LogInformation(
                "InsurancePreCheck_Incomplete: PatientId={PatientId} DurationMs={DurationMs}",
                request.PatientId,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            return classified;
        }

        // Step 2 — DB lookup: both fields are present; query DummyInsurers.
        try
        {
            // ProviderName and InsuranceId are guaranteed non-null/non-whitespace here
            // because InsuranceSoftCheckClassifier.Classify returned PendingDbLookup.
            var exists = await _dummyInsurers.ExistsAsync(
                request.ProviderName!,
                request.InsuranceId!,
                cancellationToken);

            // Step 3 — Map to final result.
            var status   = exists ? InsuranceValidationResult.Verified : InsuranceValidationResult.NotRecognized;
            var guidance = exists
                ? InsuranceSoftCheckClassifier.GuidanceVerified
                : InsuranceSoftCheckClassifier.GuidanceNotRecognized;

            _logger.LogInformation(
                "InsurancePreCheck_{Status}: PatientId={PatientId} DurationMs={DurationMs}",
                status,
                request.PatientId,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            return new InsurancePreCheckResult(status, guidance);
        }
        catch (Exception ex)
        {
            // Graceful degradation: any DB failure must not block the booking (NFR-018, FR-040).
            // PHI is deliberately omitted from the log (NFR-013, HIPAA).
            _logger.LogWarning(
                ex,
                "InsurancePreCheck_CheckPending: DummyInsurers query failed. " +
                "PatientId={PatientId} DurationMs={DurationMs}",
                request.PatientId,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            return new InsurancePreCheckResult(
                InsuranceValidationResult.CheckPending,
                InsuranceSoftCheckClassifier.GuidanceCheckPending);
        }
    }
}

using MediatR;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.Clinical.Commands;
using Serilog;

namespace Propel.Modules.Clinical.Handlers;

/// <summary>
/// Handles <see cref="DeduplicatePatientDataCommand"/> by delegating to
/// <see cref="IPatientDeduplicationService"/> and surfacing the result to the caller
/// (EP-008-I/us_041, task_003, AC-1, AC-2).
/// <para>
/// All <c>ExtractedData</c> canonical flag updates (<c>IsCanonical</c>,
/// <c>CanonicalGroupId</c>, <c>DeduplicationStatus</c>) are committed in a single
/// <c>SaveChangesAsync</c> transaction inside the service, ensuring atomicity (AC-1).
/// </para>
/// </summary>
public sealed class DeduplicatePatientDataCommandHandler
    : IRequestHandler<DeduplicatePatientDataCommand, DeduplicationResult>
{
    private readonly IPatientDeduplicationService _deduplicationService;

    public DeduplicatePatientDataCommandHandler(
        IPatientDeduplicationService deduplicationService)
    {
        _deduplicationService = deduplicationService;
    }

    /// <inheritdoc/>
    public async Task<DeduplicationResult> Handle(
        DeduplicatePatientDataCommand command,
        CancellationToken cancellationToken)
    {
        Log.Information(
            "DeduplicatePatientData_Start: patientId={PatientId}",
            command.PatientId);

        var result = await _deduplicationService.DeduplicateAsync(
            command.PatientId,
            cancellationToken);

        if (result.CircuitBreakerOpen)
        {
            Log.Warning(
                "DeduplicatePatientData_FallbackManual: patientId={PatientId} — " +
                "GPT-4o circuit breaker was open; similarity-only de-dup applied. " +
                "All affected records set to DeduplicationStatus = FallbackManual (AIR-O02).",
                command.PatientId);
        }

        if (result.ExceedsSlaThreshold)
        {
            Log.Warning(
                "DeduplicatePatientData_ExceedsSlaThreshold: patientId={PatientId} — " +
                "more than 10 source documents processed (edge case spec).",
                command.PatientId);
        }

        Log.Information(
            "DeduplicatePatientData_Complete: patientId={PatientId} " +
            "clusters={Clusters} canonical={Canonical} duplicates={Duplicates}",
            command.PatientId,
            result.ClustersFound,
            result.CanonicalSelected,
            result.DuplicatesMarked);

        return result;
    }
}

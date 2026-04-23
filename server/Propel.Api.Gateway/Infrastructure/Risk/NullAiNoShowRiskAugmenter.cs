using Propel.Modules.Risk.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Risk;

/// <summary>
/// Stub implementation of <see cref="IAiNoShowRiskAugmenter"/> used until task_003 delivers
/// the concrete AI-powered augmenter (us_031, AC-3, AIR-007).
/// Always returns a delta of <c>0.0</c> — the rule-based score is used unchanged.
/// Replaced by the real implementation when task_003 is complete.
/// </summary>
public sealed class NullAiNoShowRiskAugmenter : IAiNoShowRiskAugmenter
{
    public Task<double> GetAugmentationDeltaAsync(
        Guid patientId,
        Guid appointmentId,
        double baseScore,
        CancellationToken cancellationToken = default)
    {
        // Stub: no AI augmentation yet. Returns 0.0 so base score is used unchanged.
        return Task.FromResult(0.0);
    }
}

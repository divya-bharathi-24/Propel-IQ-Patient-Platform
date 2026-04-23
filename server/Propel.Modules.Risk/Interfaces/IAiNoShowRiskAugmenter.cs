namespace Propel.Modules.Risk.Interfaces;

/// <summary>
/// Abstraction for the AI-powered no-show risk augmentation service (us_031, task_003, AIR-007).
/// This stub interface is defined here in task_002; the concrete implementation is provided
/// by task_003 (<c>AiNoShowRiskAugmenter</c>).
/// <para>
/// AC-3 degraded-mode contract: when the AI service is unavailable, implementations MUST
/// throw <see cref="Exceptions.AiNoShowRiskUnavailableException"/> so callers can fall back
/// to the base rule-based score with <c>delta = 0.0</c>.
/// </para>
/// </summary>
public interface IAiNoShowRiskAugmenter
{
    /// <summary>
    /// Returns a score delta in the range <c>[-0.15, +0.15]</c> that adjusts the rule-based
    /// base score. Positive delta increases risk; negative delta decreases it.
    /// </summary>
    /// <exception cref="Exceptions.AiNoShowRiskUnavailableException">
    /// Thrown when the AI service circuit is open or the service is unreachable (AC-3).
    /// </exception>
    Task<double> GetAugmentationDeltaAsync(
        Guid patientId,
        Guid appointmentId,
        double baseScore,
        CancellationToken cancellationToken = default);
}

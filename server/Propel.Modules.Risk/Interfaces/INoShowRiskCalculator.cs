using Propel.Modules.Risk.Models;

namespace Propel.Modules.Risk.Interfaces;

/// <summary>
/// Abstraction for the rule-based no-show risk calculation engine (us_031, task_002, AC-1).
/// The concrete implementation <c>RuleBasedNoShowRiskCalculator</c> computes a weighted
/// score from five behavioral factors and optionally delegates to <see cref="IAiNoShowRiskAugmenter"/>
/// for a small score delta (AIR-007, AC-3).
/// </summary>
public interface INoShowRiskCalculator
{
    /// <summary>
    /// Computes the no-show risk score for the given appointment.
    /// Returns a <see cref="RiskScoreResult"/> containing the final score (0–1),
    /// severity band, factor breakdown, and whether the AI augmenter was in degraded mode.
    /// Returns <c>null</c> when the appointment does not exist.
    /// </summary>
    Task<RiskScoreResult?> CalculateAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}

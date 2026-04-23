namespace Propel.Modules.Risk.Models;

/// <summary>
/// Immutable result returned by <c>INoShowRiskCalculator.CalculateAsync</c> (us_031, task_002, AC-1).
/// </summary>
/// <param name="Score">Final risk score clamped to [0.0, 1.0]. Includes AI augmentation delta when available.</param>
/// <param name="Severity">Severity band: "Low" (&lt;0.35), "Medium" (0.35–0.70), "High" (&gt;0.70).</param>
/// <param name="Factors">Ordered list of contributing factors with individual scores and weights.</param>
/// <param name="DegradedMode">True when the AI augmenter was unavailable; base rule score used without delta (AC-3).</param>
public sealed record RiskScoreResult(
    double Score,
    string Severity,
    IReadOnlyList<RiskFactor> Factors,
    bool DegradedMode
);

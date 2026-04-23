namespace Propel.Modules.Risk.Models;

/// <summary>
/// Parsed response from the GPT-4o risk augmentation call (us_031, task_003, AIR-007).
/// Delta is clamped to <c>[-0.15, +0.15]</c> before use.
/// </summary>
/// <param name="Delta">Score adjustment in range [-0.15, +0.15]. Positive = higher risk; negative = lower risk.</param>
/// <param name="Rationale">One-sentence explanation of the adjustment produced by GPT-4o.</param>
/// <param name="Confidence">Model confidence in the adjustment; range [0.0, 1.0].</param>
public sealed record RiskAugmentationResult(
    double Delta,
    string Rationale,
    double Confidence
);

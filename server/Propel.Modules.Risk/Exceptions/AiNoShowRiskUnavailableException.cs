namespace Propel.Modules.Risk.Exceptions;

/// <summary>
/// Thrown by <c>IAiNoShowRiskAugmenter</c> implementations when the AI service circuit
/// is open or the upstream model provider is unreachable (us_031, AC-3, AIR-007).
/// <para>
/// Callers (<c>RuleBasedNoShowRiskCalculator</c>) catch this exception, apply
/// <c>delta = 0.0</c>, log a Serilog Warning with tag <c>NoShowRisk_AiDegraded</c>,
/// and set <c>RiskScoreResult.DegradedMode = true</c>.
/// </para>
/// </summary>
public sealed class AiNoShowRiskUnavailableException : Exception
{
    public AiNoShowRiskUnavailableException()
        : base("AI no-show risk augmentation service is unavailable.") { }

    public AiNoShowRiskUnavailableException(string message)
        : base(message) { }

    public AiNoShowRiskUnavailableException(string message, Exception inner)
        : base(message, inner) { }
}

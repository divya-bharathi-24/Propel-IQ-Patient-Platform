namespace Propel.Modules.Risk.Models;

/// <summary>
/// Represents a single contributing factor in the no-show risk calculation (us_031, AC-1).
/// Serialised to JSONB in the <c>no_show_risks.factors</c> column.
/// </summary>
/// <param name="Name">Factor identifier (e.g., "PriorNoShowHistory", "BookingLeadTime").</param>
/// <param name="Score">Raw factor score in [0.0, 1.0] before weighting.</param>
/// <param name="Weight">Factor weight applied in the weighted sum.</param>
/// <param name="Contribution">Weighted contribution (<c>Score × Weight</c>) added to the total.</param>
/// <param name="Note">Optional human-readable note explaining the factor value (e.g., "neutral (no history)").</param>
public sealed record RiskFactor(
    string Name,
    double Score,
    double Weight,
    double Contribution,
    string? Note = null
);

namespace Propel.Modules.Risk.Dtos;

/// <summary>
/// Embedded no-show risk payload returned within <see cref="StaffAppointmentDto"/>
/// for <c>GET /api/staff/appointments</c> (us_031, AC-1).
/// </summary>
/// <param name="Score">Risk score in [0.0, 1.0].</param>
/// <param name="Severity">Severity band: "Low", "Medium", or "High".</param>
/// <param name="Factors">JSONB factor breakdown serialised for display.</param>
/// <param name="CalculatedAt">UTC timestamp when the score was last computed.</param>
public sealed record NoShowRiskDto(
    decimal Score,
    string Severity,
    object Factors,
    DateTime CalculatedAt
);

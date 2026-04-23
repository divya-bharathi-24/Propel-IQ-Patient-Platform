using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Represents a recommended intervention row generated for a High-risk appointment (US_032, FR-030).
/// <para>
/// Two rows (<c>AdditionalReminder</c> and <c>CallbackRequest</c>) are inserted when the
/// no-show risk score crosses the High threshold (&gt; 0.66). Both start with
/// <see cref="InterventionStatus.Pending"/> and are acknowledged by Staff via Accept/Dismiss.
/// </para>
/// <para>
/// <c>AppointmentId</c> is stored directly (in addition to <c>NoShowRiskId</c>) to allow
/// efficient querying by appointment without a join through <c>no_show_risks</c>.
/// </para>
/// </summary>
public sealed class RiskIntervention
{
    public Guid Id { get; set; }

    /// <summary>FK to <c>appointments</c> — enables direct filtering without join (US_032, AC-4).</summary>
    public Guid AppointmentId { get; set; }

    /// <summary>FK to <c>no_show_risks</c> — the risk record that triggered this intervention.</summary>
    public Guid NoShowRiskId { get; set; }

    /// <summary>The type of recommended intervention (e.g., AdditionalReminder, CallbackRequest).</summary>
    public InterventionType Type { get; set; }

    /// <summary>Lifecycle status — starts as Pending, transitions to Accepted/Dismissed/AutoCleared.</summary>
    public InterventionStatus Status { get; set; }

    /// <summary>
    /// The Staff member who acknowledged this intervention (Accept or Dismiss).
    /// Sourced exclusively from the JWT <c>NameIdentifier</c> claim — never from request body (OWASP A01).
    /// <c>null</c> until acknowledgement.
    /// </summary>
    public Guid? StaffId { get; set; }

    /// <summary>UTC timestamp when the intervention was accepted or dismissed. <c>null</c> while Pending.</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Optional free-text reason supplied by Staff when dismissing (max 500 chars, AC-3).</summary>
    public string? DismissalReason { get; set; }

    /// <summary>UTC timestamp when this intervention row was created. DB default: <c>NOW()</c>.</summary>
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Appointment Appointment { get; set; } = null!;
    public NoShowRisk NoShowRisk { get; set; } = null!;

    /// <summary>
    /// The Staff <see cref="User"/> who acknowledged this intervention.
    /// <c>null</c> while <see cref="Status"/> is <see cref="InterventionStatus.Pending"/>.
    /// FK uses <c>SET NULL</c>: deleting the User record retains the intervention for audit history.
    /// </summary>
    public User? Staff { get; set; }
}

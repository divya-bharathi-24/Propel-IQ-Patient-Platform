using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Insurance validation record entity storing the result of verifying a patient's insurance
/// coverage against a provider, optionally linked to a specific appointment (DR-014).
/// All mapping is deferred to EF fluent configuration in <c>PropelIQ.Infrastructure</c>.
/// </summary>
public sealed class InsuranceValidation
{
    public Guid Id { get; set; }

    /// <summary>The patient whose insurance was validated.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Optional appointment the validation was performed in context of.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>Name of the insurance provider queried during validation.</summary>
    public required string ProviderName { get; set; }

    /// <summary>Insurance policy or member identifier supplied by the patient.</summary>
    public required string InsuranceId { get; set; }

    /// <summary>Outcome of the validation check.</summary>
    public InsuranceValidationResult ValidationResult { get; set; }

    /// <summary>Human-readable message accompanying the validation result, if any.</summary>
    public string? ValidationMessage { get; set; }

    /// <summary>UTC timestamp when the validation was completed, or null if still pending.</summary>
    public DateTime? ValidatedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
}

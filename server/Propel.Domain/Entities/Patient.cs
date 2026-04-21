using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Patient domain entity representing a registered patient account.
/// Email uniqueness is enforced via a unique index defined in EF fluent configuration (task_002).
/// Soft-delete is implemented via <see cref="Status"/> — records are never hard-deleted (DR-010).
/// </summary>
public sealed class Patient
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Phone { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public required string PasswordHash { get; set; }
    public bool EmailVerified { get; set; }

    /// <summary>Soft-delete state — never hard DELETE (DR-010).</summary>
    public PatientStatus Status { get; set; } = PatientStatus.Active;

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<WaitlistEntry> WaitlistEntries { get; set; } = new List<WaitlistEntry>();
}

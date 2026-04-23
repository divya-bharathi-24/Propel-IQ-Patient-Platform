using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Patient domain entity representing a registered patient account.
/// Email uniqueness is enforced via a unique index defined in EF fluent configuration (task_002).
/// Soft-delete is implemented via <see cref="Status"/> — records are never hard-deleted (DR-010).
///
/// Demographic fields added for US_015 (Patient Profile View &amp; Structured Demographic Edit):
/// <list type="bullet">
///   <item><see cref="BiologicalSex"/> — locked field (read-only post registration).</item>
///   <item><see cref="AddressEncrypted"/> — AES-256 encrypted JSON (PHI, NFR-004); non-locked.</item>
///   <item><see cref="EmergencyContactEncrypted"/> — AES-256 encrypted JSON (PHI, NFR-004); non-locked.</item>
///   <item><see cref="CommunicationPreferencesJson"/> — plain JSON (non-PHI); non-locked.</item>
///   <item><see cref="InsurerName"/>, <see cref="MemberId"/>, <see cref="GroupNumber"/> — non-locked.</item>
///   <item><see cref="RowVersion"/> — maps to PostgreSQL <c>xmin</c> for optimistic concurrency (AC-4).</item>
/// </list>
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

    // ── US_015 demographic extension fields ──────────────────────────────────

    /// <summary>Locked field — set at registration, never mutated via PATCH /api/patients/me (AC-3).</summary>
    public string? BiologicalSex { get; set; }

    /// <summary>
    /// AES-256 encrypted JSON representation of the patient's address (PHI — NFR-004).
    /// Handlers are responsible for encrypting before write and decrypting after read
    /// via <c>IPhiEncryptionService</c>. Stored as TEXT column.
    /// </summary>
    public string? AddressEncrypted { get; set; }

    /// <summary>
    /// AES-256 encrypted JSON representation of the emergency contact (PHI — NFR-004).
    /// Handlers are responsible for encrypting before write and decrypting after read.
    /// Stored as TEXT column.
    /// </summary>
    public string? EmergencyContactEncrypted { get; set; }

    /// <summary>
    /// Plain JSON representation of communication opt-in flags (non-PHI).
    /// Handlers are responsible for serializing before write and deserializing after read.
    /// Stored as JSONB column.
    /// </summary>
    public string? CommunicationPreferencesJson { get; set; }

    /// <summary>Insurance carrier name. Non-PHI.</summary>
    public string? InsurerName { get; set; }

    /// <summary>Insurance member identifier. Non-PHI.</summary>
    public string? MemberId { get; set; }

    /// <summary>Insurance group number. Non-PHI.</summary>
    public string? GroupNumber { get; set; }

    /// <summary>
    /// Optimistic concurrency token mapped to PostgreSQL <c>xmin</c> system column (AC-4, US_015).
    /// Must be <c>uint</c> for Npgsql xmin concurrency support. No migration required — xmin
    /// exists on all PostgreSQL tables automatically.
    /// </summary>
    public uint RowVersion { get; set; }

    /// <summary>
    /// Plain JSON array of pending alert objects surfaced on next patient login (US_025, AC-dual-failure).
    /// Each element has the shape <c>{ alertType, appointmentId, createdAt }</c>.
    /// Written only when both email and SMS notification dispatch fail for a slot swap.
    /// Stored as JSONB column; non-PHI.
    /// </summary>
    public string? PendingAlertsJson { get; set; }

    /// <summary>
    /// UTC timestamp at which a staff member verified the patient's 360-degree clinical data view (FR-047, US_016).
    /// NULL means the patient has not yet been verified. Derived boolean <c>viewVerified = ViewVerifiedAt IS NOT NULL</c>
    /// is exposed in the dashboard response (AC-4). Column and migration are managed by US_016 / TASK_003.
    /// </summary>
    public DateTime? ViewVerifiedAt { get; set; }

    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<WaitlistEntry> WaitlistEntries { get; set; } = new List<WaitlistEntry>();
}

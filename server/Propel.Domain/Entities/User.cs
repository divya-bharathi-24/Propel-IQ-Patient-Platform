using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// User domain entity representing an authenticated staff or admin account.
/// Patients authenticate through <see cref="Patient"/>; this entity covers Staff and Admin roles.
/// Soft-delete is implemented via <see cref="Status"/> — records are never hard-deleted (DR-010).
/// A new user account has <see cref="PasswordHash"/> = <c>null</c> until credentials are set
/// via <c>POST /api/auth/setup-credentials</c> (US_012, AC-2).
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }

    /// <summary>
    /// Argon2id hash of the user's password. Null until the user completes the
    /// credential setup flow via their invite token (US_012, AC-2).
    /// </summary>
    public string? PasswordHash { get; set; }

    public UserRole Role { get; set; }

    /// <summary>Soft-delete state — reuses PatientStatus Active/Deactivated values (DR-010).</summary>
    public PatientStatus Status { get; set; } = PatientStatus.Active;

    /// <summary>
    /// Tracks the SendGrid delivery state of the credential setup invite email.
    /// Values: "Pending" | "Sent" | "Failed" | "Bounced" (AC-1 edge case).
    /// </summary>
    public string CredentialEmailStatus { get; set; } = "Pending";

    public string? Name { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public ICollection<CredentialSetupToken> CredentialSetupTokens { get; set; } = new List<CredentialSetupToken>();
}

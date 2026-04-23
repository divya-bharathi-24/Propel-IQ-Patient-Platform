using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="Patient"/> persistence.
/// Implementations live in the infrastructure layer (Propel.Api.Gateway).
/// </summary>
public interface IPatientRepository
{
    /// <summary>
    /// Returns <c>true</c> when a patient with the given email already exists
    /// (case-insensitive). Used for duplicate-email detection (AC-3).
    /// </summary>
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Creates a new patient record and returns the persisted entity.</summary>
    Task<Patient> CreateAsync(Patient patient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a patient by email (case-insensitive). Returns <c>null</c> when not found.
    /// </summary>
    Task<Patient?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a patient by primary key. Returns <c>null</c> when not found.
    /// </summary>
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <see cref="Patient.EmailVerified"/> to <c>true</c> and persists the change.
    /// Because both <see cref="Patient"/> and <see cref="EmailVerificationToken"/> are tracked
    /// by the same scoped <c>AppDbContext</c>, this call also commits any pending token mutations
    /// (e.g. <c>UsedAt</c>) in a single <c>SaveChangesAsync</c> call.
    /// </summary>
    Task MarkEmailVerifiedAsync(Patient patient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists mutations applied to a tracked <see cref="Patient"/> entity (US_015, AC-2).
    /// EF Core value converters transparently re-encrypt PHI fields before writing (NFR-004).
    /// Callers must set only non-locked fields; locked fields are the caller's responsibility to leave unchanged.
    /// </summary>
    Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the patient's contact details and communication preferences required for
    /// notification dispatch (US_025, AC-1, AC-4).
    /// Returns a tuple of (Email, Name, Phone, CommunicationPreferencesJson) or <c>null</c>
    /// when the patient is not found.
    /// </summary>
    Task<(string Email, string Name, string Phone, string? CommunicationPreferencesJson)?>
        GetCommunicationPreferencesAsync(Guid patientId, CancellationToken cancellationToken = default);
}

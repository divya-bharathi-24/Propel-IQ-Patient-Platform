using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="PatientProfileVerification"/> records (AC-3, task_002).
/// </summary>
public interface IPatientProfileVerificationRepository
{
    /// <summary>
    /// Returns the verification record for <paramref name="patientId"/>, or <c>null</c> if none exists.
    /// </summary>
    Task<PatientProfileVerification?> GetByPatientIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the verification record for the given patient (one record per patient).
    /// </summary>
    Task UpsertAsync(
        PatientProfileVerification verification,
        CancellationToken cancellationToken = default);
}

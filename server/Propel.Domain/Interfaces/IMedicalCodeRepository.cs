using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository for reading and persisting <see cref="MedicalCode"/> records used by the Staff
/// code-confirmation workflow (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
/// <para>
/// Follows the same interface-per-aggregate pattern used by <see cref="IAuditLogRepository"/>,
/// <see cref="IPatientProfileVerificationRepository"/>, etc. (AD-1 hexagonal architecture).
/// The EF Core implementation lives in the Gateway project to prevent module → gateway circular
/// dependencies.
/// </para>
/// </summary>
public interface IMedicalCodeRepository
{
    /// <summary>
    /// Returns all <see cref="MedicalCode"/> records whose <see cref="MedicalCode.Id"/> is
    /// contained in <paramref name="ids"/> and whose <see cref="MedicalCode.PatientId"/>
    /// matches <paramref name="patientId"/> (prevents cross-patient data access — OWASP A01).
    /// </summary>
    Task<List<MedicalCode>> GetByIdsAndPatientAsync(
        Guid patientId,
        IReadOnlySet<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new <see cref="MedicalCode"/> for insertion. Caller must call
    /// <see cref="SaveAsync"/> to flush the change.
    /// </summary>
    Task AddAsync(MedicalCode code, CancellationToken cancellationToken);

    /// <summary>Persists all pending changes to the database.</summary>
    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of <see cref="MedicalCode"/> records for <paramref name="patientId"/>
    /// that still have <see cref="MedicalCodeVerificationStatus.Pending"/> status.
    /// </summary>
    Task<int> CountPendingAsync(Guid patientId, CancellationToken cancellationToken);
}

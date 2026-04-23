namespace Propel.Domain.Interfaces;

/// <summary>
/// Read-only repository for querying the <c>DummyInsurers</c> seed table (US_022, task_002).
/// Used exclusively by the insurance pre-check endpoint — no write operations are exposed.
/// </summary>
public interface IDummyInsurersRepository
{
    /// <summary>
    /// Returns <c>true</c> when an active <c>DummyInsurer</c> record matches both
    /// <paramref name="providerName"/> (case-insensitive) and the patient's
    /// <paramref name="insuranceId"/> starts with the record's <c>MemberIdPrefix</c>
    /// (case-insensitive). Returns <c>false</c> when no match exists.
    /// </summary>
    /// <param name="providerName">Insurance provider name submitted by the patient.</param>
    /// <param name="insuranceId">Member ID submitted by the patient.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<bool> ExistsAsync(
        string providerName,
        string insuranceId,
        CancellationToken cancellationToken = default);
}

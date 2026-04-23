using Propel.Domain.Enums;

namespace Propel.Modules.Appointment.Infrastructure;

/// <summary>
/// Performs a non-blocking insurance soft-check against the <c>DummyInsurers</c> seed table
/// (US_019, AC-2, FR-040). Returns an <see cref="InsuranceValidationResult"/> without throwing;
/// any infrastructure failure returns <see cref="InsuranceValidationResult.CheckPending"/>
/// so the booking always proceeds (NFR-018 graceful degradation).
/// </summary>
public interface IInsuranceSoftCheckService
{
    /// <summary>
    /// Checks whether <paramref name="insuranceName"/> and <paramref name="insuranceId"/> match
    /// an active record in the <c>DummyInsurers</c> seed table (case-insensitive).
    /// <list type="bullet">
    ///   <item>Either field null/empty → <see cref="InsuranceValidationResult.Incomplete"/>.</item>
    ///   <item>Match found → <see cref="InsuranceValidationResult.Verified"/>.</item>
    ///   <item>No match → <see cref="InsuranceValidationResult.NotRecognized"/>.</item>
    ///   <item>Any database exception → <see cref="InsuranceValidationResult.CheckPending"/>.</item>
    /// </list>
    /// </summary>
    Task<InsuranceValidationResult> CheckAsync(
        string? insuranceName,
        string? insuranceId,
        CancellationToken cancellationToken = default);
}

using Propel.Domain.Enums;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Services;

/// <summary>
/// Pure classification service for <c>POST /api/insurance/pre-check</c> (US_022, task_002).
/// <para>
/// Contains no database dependency — all guidance text constants live here as the single
/// source of truth (never duplicated in the controller or frontend).
/// Extracted from the handler to keep the handler thin and to enable isolated unit testing
/// of the classification rules.
/// </para>
/// </summary>
public sealed class InsuranceSoftCheckClassifier
{
    // ── Guidance text constants (single source of truth — NFR-013, AC-1, AC-2) ──

    /// <summary>Displayed when both fields match an active DummyInsurer record.</summary>
    public const string GuidanceVerified =
        "Your insurance has been verified successfully.";

    /// <summary>Displayed when both fields are present but no DummyInsurer record matched.</summary>
    public const string GuidanceNotRecognized =
        "Your insurer was not found in our records. You can still complete your booking — " +
        "please bring your insurance card to your appointment.";

    /// <summary>Displayed when one or both required fields are missing.</summary>
    private const string GuidanceIncompleteTemplate =
        "Please provide your {0} to complete the insurance check, or skip this step to proceed with your booking.";

    /// <summary>Displayed when the DummyInsurers DB query throws any exception (NFR-018, FR-040).</summary>
    public const string GuidanceCheckPending =
        "Insurance check is temporarily unavailable. Your booking can proceed and our staff will verify your insurance at the appointment.";

    /// <summary>
    /// Sentinel result indicating both fields are present and a database lookup is required.
    /// Never returned to the API client — the handler replaces it with a real DB-backed result.
    /// </summary>
    public static readonly InsurancePreCheckResult PendingDbLookup =
        new(InsuranceValidationResult.CheckPending, GuidanceCheckPending);

    /// <summary>
    /// Classifies the insurance pre-check without touching the database (US_022, AC-1, AC-4).
    /// <list type="bullet">
    ///   <item>Either field null/empty → returns <see cref="InsuranceValidationResult.Incomplete"/>
    ///         with guidance identifying the specific missing field(s).</item>
    ///   <item>Both fields present → returns <see cref="PendingDbLookup"/> to signal the handler
    ///         that a database lookup is required.</item>
    /// </list>
    /// </summary>
    /// <param name="providerName">Insurance provider name; may be null or whitespace.</param>
    /// <param name="insuranceId">Member ID; may be null or whitespace.</param>
    /// <returns>
    /// An <see cref="InsurancePreCheckResult"/> with <c>Incomplete</c> status when a field is
    /// missing, or <see cref="PendingDbLookup"/> when both fields are present.
    /// </returns>
    public InsurancePreCheckResult Classify(string? providerName, string? insuranceId)
    {
        bool namePresent = !string.IsNullOrWhiteSpace(providerName);
        bool idPresent   = !string.IsNullOrWhiteSpace(insuranceId);

        if (!namePresent || !idPresent)
        {
            string missing = (!namePresent && !idPresent)
                ? "insurer name and member ID"
                : !namePresent
                    ? "insurer name"
                    : "member ID";

            return new InsurancePreCheckResult(
                InsuranceValidationResult.Incomplete,
                string.Format(GuidanceIncompleteTemplate, missing));
        }

        // Both fields present — signal that a DB lookup is required.
        return PendingDbLookup;
    }
}

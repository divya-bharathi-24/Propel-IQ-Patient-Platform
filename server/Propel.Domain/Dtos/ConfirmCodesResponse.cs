namespace Propel.Domain.Dtos;

/// <summary>
/// Response returned by <c>POST /api/medical-codes/confirm</c> (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
/// <para>
/// Summarises the outcome of a bulk code-review submission and includes <see cref="PendingCount"/>
/// so the frontend can display a "X codes pending review" progress indicator (edge case: partial
/// submission allowed; unreferenced codes retain <c>VerificationStatus = Pending</c>).
/// </para>
/// </summary>
public sealed record ConfirmCodesResponse(
    /// <summary>Number of codes whose <c>VerificationStatus</c> was set to <c>Accepted</c>.</summary>
    int AcceptedCount,

    /// <summary>Number of codes whose <c>VerificationStatus</c> was set to <c>Rejected</c>.</summary>
    int RejectedCount,

    /// <summary>Number of new codes inserted with <c>IsManualEntry = true</c>.</summary>
    int ManualCount,

    /// <summary>
    /// Count of <see cref="Propel.Domain.Entities.MedicalCode"/> records for this patient that
    /// still have <c>VerificationStatus = Pending</c> after the submission (AC-2 edge case).
    /// </summary>
    int PendingCount);

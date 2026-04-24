namespace Propel.Domain.Dtos;

/// <summary>
/// HTTP request body for <c>POST /api/medical-codes/confirm</c>
/// (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
/// <para>
/// The <c>StaffUserId</c> is NOT included here — it is sourced exclusively from the verified JWT
/// claim in <c>MedicalCodesController</c> to prevent IDOR attacks (OWASP A01).
/// </para>
/// </summary>
public sealed record ConfirmMedicalCodesRequest(
    /// <summary>Patient whose codes are being reviewed.</summary>
    Guid PatientId,

    /// <summary>IDs of <see cref="Propel.Domain.Entities.MedicalCode"/> records to accept.</summary>
    IReadOnlyList<Guid> Accepted,

    /// <summary>IDs and rejection reasons for codes to reject.</summary>
    IReadOnlyList<RejectedCodeEntry> Rejected,

    /// <summary>New codes entered manually by Staff; inserted with <c>IsManualEntry = true</c>.</summary>
    IReadOnlyList<ManualCodeEntry> Manual);

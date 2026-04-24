using MediatR;
using Propel.Domain.Dtos;

namespace Propel.Modules.Clinical.Commands;

/// <summary>
/// MediatR command: bulk accept/reject/manual-entry decision for AI-suggested medical codes
/// (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
/// <para>
/// Sent by <c>MedicalCodesController.ConfirmMedicalCodes</c> in response to
/// <c>POST /api/medical-codes/confirm</c> (RBAC: Staff — NFR-006).
/// </para>
/// <para>
/// <see cref="StaffUserId"/> is sourced exclusively from the verified JWT claim in the controller —
/// never from the request body (OWASP A01: Broken Access Control).
/// </para>
/// <para>
/// Codes not referenced in any list retain <c>VerificationStatus = Pending</c> (partial submission
/// is allowed per the edge-case specification).
/// </para>
/// </summary>
public sealed record ConfirmMedicalCodesCommand(
    /// <summary>Patient whose codes are being reviewed.</summary>
    Guid PatientId,

    /// <summary>
    /// Authenticated Staff member performing the review. Sourced from JWT — never from the
    /// request body (OWASP A01).
    /// </summary>
    Guid StaffUserId,

    /// <summary>IDs of <c>MedicalCode</c> records to accept (<c>VerificationStatus → Accepted</c>).</summary>
    IReadOnlyList<Guid> Accepted,

    /// <summary>IDs and rejection reasons for codes to reject (<c>VerificationStatus → Rejected</c>).</summary>
    IReadOnlyList<RejectedCodeEntry> Rejected,

    /// <summary>New codes entered manually by Staff; inserted with <c>IsManualEntry = true</c>.</summary>
    IReadOnlyList<ManualCodeEntry> Manual) : IRequest<ConfirmCodesResponse>;

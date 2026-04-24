using MediatR;
using Propel.Domain.Dtos;
using Propel.Domain.Enums;

namespace Propel.Modules.Clinical.Commands;

/// <summary>
/// MediatR command: validates a single raw medical code against the ICD-10-CM or CPT reference
/// library (EP-008-II/us_043, task_002, AC-4).
/// <para>
/// Sent by <c>MedicalCodesController.ValidateMedicalCode</c> in response to
/// <c>POST /api/medical-codes/validate</c>. The controller does NOT include any user-supplied
/// identity data — the validation is code-content-only (OWASP A01).
/// </para>
/// </summary>
public sealed record ValidateMedicalCodeCommand(
    /// <summary>Raw code string submitted by the Staff user (e.g. "J18.9", "99213").</summary>
    string Code,

    /// <summary>Code system to validate against: <see cref="MedicalCodeType.ICD10"/> or <see cref="MedicalCodeType.CPT"/>.</summary>
    MedicalCodeType CodeType) : IRequest<CodeValidationResult>;

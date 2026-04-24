using Propel.Domain.Enums;

namespace Propel.Domain.Dtos;

/// <summary>
/// Identifies a medical code to reject and carries the staff-provided reason (AC-3, FR-053).
/// Used as part of <c>POST /api/medical-codes/confirm</c> request and
/// <see cref="Propel.Modules.Clinical.Commands.ConfirmMedicalCodesCommand"/>.
/// </summary>
public sealed record RejectedCodeEntry(
    /// <summary>Primary key of the <c>MedicalCode</c> record to reject.</summary>
    Guid Id,

    /// <summary>Staff-provided rejection reason; stored in <c>MedicalCode.RejectionReason</c>.</summary>
    string RejectionReason);

/// <summary>
/// A manually entered medical code submitted by Staff that was not produced by the AI pipeline
/// (AC-4, DR-007). Persisted as a new <c>MedicalCode</c> row with <c>IsManualEntry = true</c>.
/// </summary>
public sealed record ManualCodeEntry(
    /// <summary>ICD-10-CM or CPT code string (pre-validated via <c>POST /validate</c>).</summary>
    string Code,

    /// <summary>Code system for this entry.</summary>
    MedicalCodeType CodeType,

    /// <summary>Human-readable code description entered or confirmed by the Staff user.</summary>
    string Description);

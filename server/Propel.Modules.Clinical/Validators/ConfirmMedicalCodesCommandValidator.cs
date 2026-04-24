using FluentValidation;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Validators;

/// <summary>
/// FluentValidation validator for <see cref="ConfirmMedicalCodesCommand"/>
/// (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
/// <list type="bullet">
///   <item><c>PatientId</c> must be a non-empty GUID.</item>
///   <item><c>StaffUserId</c> must be a non-empty GUID (sourced from JWT — validated here as a safety net).</item>
///   <item>At least one of <c>Accepted</c>, <c>Rejected</c>, or <c>Manual</c> must be non-empty.</item>
///   <item>Each entry in <c>Rejected</c> must have a non-empty <c>RejectionReason</c>.</item>
///   <item>Each <c>Manual</c> entry must have a non-empty <c>Code</c> (max 10 chars), non-empty <c>Description</c>, and a valid <c>CodeType</c>.</item>
/// </list>
/// </summary>
public sealed class ConfirmMedicalCodesCommandValidator : AbstractValidator<ConfirmMedicalCodesCommand>
{
    public ConfirmMedicalCodesCommandValidator()
    {
        RuleFor(c => c.PatientId)
            .NotEmpty()
            .WithMessage("PatientId must be a valid non-empty GUID.");

        RuleFor(c => c.StaffUserId)
            .NotEmpty()
            .WithMessage("StaffUserId must be a valid non-empty GUID.");

        // At least one decision list must be non-empty (partial submission is fine, but a
        // completely empty payload has no effect and signals a client error).
        RuleFor(c => c)
            .Must(c => c.Accepted.Count > 0 || c.Rejected.Count > 0 || c.Manual.Count > 0)
            .WithMessage("At least one of Accepted, Rejected, or Manual must contain entries.");

        // Each rejection must carry a reason (AC-3)
        RuleForEach(c => c.Rejected)
            .ChildRules(r =>
            {
                r.RuleFor(e => e.Id)
                 .NotEmpty()
                 .WithMessage("Each rejected entry must have a valid non-empty Id.");

                r.RuleFor(e => e.RejectionReason)
                 .NotEmpty()
                 .WithMessage("Each rejected entry must include a RejectionReason.")
                 .MaximumLength(500)
                 .WithMessage("RejectionReason must not exceed 500 characters.");
            });

        // Each manual entry must be a valid code reference (AC-4)
        RuleForEach(c => c.Manual)
            .ChildRules(m =>
            {
                m.RuleFor(e => e.Code)
                 .NotEmpty()
                 .WithMessage("Manual entry Code must not be empty.")
                 .MaximumLength(10)
                 .WithMessage("Manual entry Code must not exceed 10 characters.");

                m.RuleFor(e => e.Description)
                 .NotEmpty()
                 .WithMessage("Manual entry Description must not be empty.")
                 .MaximumLength(500)
                 .WithMessage("Manual entry Description must not exceed 500 characters.");

                m.RuleFor(e => e.CodeType)
                 .IsInEnum()
                 .WithMessage("Manual entry CodeType must be ICD10 or CPT.");
            });
    }
}

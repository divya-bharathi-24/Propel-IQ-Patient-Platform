using FluentValidation;
using Propel.Domain.Enums;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Validators;

/// <summary>
/// FluentValidation validator for <see cref="ValidateMedicalCodeCommand"/> (EP-008-II/us_043, task_002, AC-4).
/// <list type="bullet">
///   <item><c>Code</c> must be a non-empty string with at most 10 characters.</item>
///   <item><c>CodeType</c> must be a defined <see cref="MedicalCodeType"/> value (ICD10 or CPT).</item>
/// </list>
/// </summary>
public sealed class ValidateMedicalCodeCommandValidator : AbstractValidator<ValidateMedicalCodeCommand>
{
    public ValidateMedicalCodeCommandValidator()
    {
        RuleFor(c => c.Code)
            .NotEmpty()
            .WithMessage("Code must not be empty.")
            .MaximumLength(10)
            .WithMessage("Code must not exceed 10 characters.");

        RuleFor(c => c.CodeType)
            .IsInEnum()
            .WithMessage("CodeType must be ICD10 or CPT.");
    }
}

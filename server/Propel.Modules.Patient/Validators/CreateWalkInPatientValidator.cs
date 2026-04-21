using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateWalkInPatientCommand"/>.
/// Phone is optional for walk-in patients (US_012, AC-3).
/// </summary>
public sealed class CreateWalkInPatientValidator : AbstractValidator<CreateWalkInPatientCommand>
{
    public CreateWalkInPatientValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid email address.")
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        RuleFor(x => x.Phone)
            .Matches(@"^\+?[1-9]\d{1,14}$")
                .WithMessage("Phone number is not valid (E.164 format expected).")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

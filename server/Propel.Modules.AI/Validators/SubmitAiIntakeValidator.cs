using FluentValidation;
using Propel.Modules.AI.Commands;

namespace Propel.Modules.AI.Validators;

/// <summary>
/// FluentValidation validator for <see cref="SubmitAiIntakeCommand"/> (US_028, AC-4).
/// Validates fields present in the command; session ownership, field presence, and
/// duplicate detection are enforced in <c>SubmitAiIntakeCommandHandler</c>.
/// </summary>
public sealed class SubmitAiIntakeValidator : AbstractValidator<SubmitAiIntakeCommand>
{
    public SubmitAiIntakeValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("SessionId is required.");

        RuleFor(x => x.PatientId)
            .NotEmpty()
            .WithMessage("PatientId is required.");
    }
}

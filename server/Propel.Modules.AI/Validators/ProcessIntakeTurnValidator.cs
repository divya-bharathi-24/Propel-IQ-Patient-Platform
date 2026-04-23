using FluentValidation;
using Propel.Modules.AI.Commands;

namespace Propel.Modules.AI.Validators;

/// <summary>
/// FluentValidation validator for <see cref="ProcessIntakeTurnCommand"/> (US_028, AC-2).
/// Validates fields present in the request body; session ownership and expiry are
/// checked in <c>ProcessIntakeTurnCommandHandler</c> as business rules.
/// </summary>
public sealed class ProcessIntakeTurnValidator : AbstractValidator<ProcessIntakeTurnCommand>
{
    public ProcessIntakeTurnValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("SessionId is required.");

        RuleFor(x => x.UserMessage)
            .NotEmpty()
            .WithMessage("UserMessage is required.");
    }
}

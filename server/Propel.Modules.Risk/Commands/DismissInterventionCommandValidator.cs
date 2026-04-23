using FluentValidation;
using Propel.Modules.Risk.Commands;

namespace Propel.Modules.Risk.Commands;

/// <summary>
/// FluentValidation validator for <see cref="DismissInterventionCommand"/> (US_032, AC-3).
/// Auto-discovered by <c>AddValidatorsFromAssemblies</c> in <c>Program.cs</c> and invoked
/// by <c>ValidationBehavior</c> before the handler executes — returns HTTP 422 when
/// <c>DismissalReason</c> exceeds 500 characters.
/// </summary>
public sealed class DismissInterventionCommandValidator : AbstractValidator<DismissInterventionCommand>
{
    public DismissInterventionCommandValidator()
    {
        RuleFor(x => x.InterventionId)
            .NotEmpty()
            .WithMessage("InterventionId is required.");

        RuleFor(x => x.DismissalReason)
            .MaximumLength(500)
            .WithMessage("DismissalReason must not exceed 500 characters.")
            .When(x => x.DismissalReason is not null);
    }
}

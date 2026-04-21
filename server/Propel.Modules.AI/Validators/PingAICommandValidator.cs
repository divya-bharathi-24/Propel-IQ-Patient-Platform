using FluentValidation;
using Propel.Modules.AI.Commands;

namespace Propel.Modules.AI.Validators;

public sealed class PingAICommandValidator : AbstractValidator<PingAICommand>
{
    public PingAICommandValidator()
    {
        // PingAICommand has no properties to validate.
    }
}

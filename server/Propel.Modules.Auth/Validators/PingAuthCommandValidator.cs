using FluentValidation;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Validators;

public sealed class PingAuthCommandValidator : AbstractValidator<PingAuthCommand>
{
    public PingAuthCommandValidator()
    {
        // PingAuthCommand has no properties to validate.
        // This validator exists to confirm FluentValidation wiring per AC2.
    }
}

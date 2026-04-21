using FluentValidation;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Validators;

public sealed class PingClinicalCommandValidator : AbstractValidator<PingClinicalCommand>
{
    public PingClinicalCommandValidator()
    {
        // PingClinicalCommand has no properties to validate.
    }
}

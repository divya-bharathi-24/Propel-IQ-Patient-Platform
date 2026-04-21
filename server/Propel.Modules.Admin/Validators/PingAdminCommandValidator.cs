using FluentValidation;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Validators;

public sealed class PingAdminCommandValidator : AbstractValidator<PingAdminCommand>
{
    public PingAdminCommandValidator()
    {
        // PingAdminCommand has no properties to validate.
    }
}

using FluentValidation;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RefreshTokenCommand"/>.
/// Returns HTTP 400 on violation before the handler is reached.
/// </summary>
public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("RefreshToken is required.");

        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("DeviceId is required.")
            .MaximumLength(256).WithMessage("DeviceId must not exceed 256 characters.");
    }
}

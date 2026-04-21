using FluentValidation;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Validators;

/// <summary>
/// FluentValidation validator for <see cref="LoginCommand"/>.
/// Returns HTTP 400 with structured errors on violation; never exposes which field caused the
/// authentication failure (OWASP A07 — no user enumeration).
/// </summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("DeviceId is required.")
            .MaximumLength(256).WithMessage("DeviceId must not exceed 256 characters.");
    }
}

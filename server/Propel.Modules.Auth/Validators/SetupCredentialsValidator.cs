using FluentValidation;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Validators;

/// <summary>
/// FluentValidation validator for <see cref="SetupCredentialsCommand"/>.
/// Each password complexity rule is expressed as a separate <c>Must()</c> clause
/// so that per-rule error messages surface to the client (US_012, AC-2).
/// Mirrors the pattern used by <see cref="RegistrationRequestValidator"/> (NFR-008).
/// </summary>
public sealed class SetupCredentialsValidator : AbstractValidator<SetupCredentialsCommand>
{
    public SetupCredentialsValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Setup token is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Must(ContainUppercase)
                .WithMessage("Password must contain at least one uppercase letter.")
            .Must(ContainDigit)
                .WithMessage("Password must contain at least one digit.")
            .Must(ContainSpecialCharacter)
                .WithMessage("Password must contain at least one special character (!@#$%^&*()_+-=[]{}|;':\",./<>?).");
    }

    private static bool ContainUppercase(string password)
        => password.Any(char.IsUpper);

    private static bool ContainDigit(string password)
        => password.Any(char.IsDigit);

    private static bool ContainSpecialCharacter(string password)
        => password.Any(c => "!@#$%^&*()_+-=[]{}|;':\",./<>?".Contains(c));
}

using FluentValidation;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RegisterPatientCommand"/>.
/// Each password complexity rule is expressed as a separate <c>Must()</c> clause
/// so that per-rule error messages surface to the client (AC-4).
/// No stack traces or internal details are exposed — validation errors are mapped
/// to HTTP 400 with ProblemDetails by the global exception filter.
/// </summary>
public sealed class RegistrationRequestValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegistrationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid email address.")
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Must(ContainUppercase)
                .WithMessage("Password must contain at least one uppercase letter.")
            .Must(ContainDigit)
                .WithMessage("Password must contain at least one digit.")
            .Must(ContainSpecialCharacter)
                .WithMessage("Password must contain at least one special character (!@#$%^&*()_+-=[]{}|;':\",./<>?).");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Phone)
            .Matches(@"^\+?[1-9]\d{1,14}$")
                .WithMessage("Phone number is not valid (E.164 format expected).")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required.")
            .Must(BeInThePast).WithMessage("Date of birth must be in the past.")
            .Must(BeWithinReasonableAge).WithMessage("Date of birth must be within the last 130 years.");
    }

    private static bool ContainUppercase(string password)
        => password.Any(char.IsUpper);

    private static bool ContainDigit(string password)
        => password.Any(char.IsDigit);

    private static bool ContainSpecialCharacter(string password)
        => password.Any(c => "!@#$%^&*()_+-=[]{}|;':\",./<>?".Contains(c));

    private static bool BeInThePast(DateOnly dob)
        => dob < DateOnly.FromDateTime(DateTime.UtcNow);

    private static bool BeWithinReasonableAge(DateOnly dob)
        => dob >= DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-130));
}

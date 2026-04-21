using FluentValidation;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateUserAccountCommand"/>.
/// Validates name, email, and role fields as specified in US_012, AC-1.
/// Validation errors map to HTTP 400 with ProblemDetails via the global exception filter.
/// </summary>
public sealed class CreateUserAccountValidator : AbstractValidator<CreateUserAccountCommand>
{
    private static readonly HashSet<string> ValidRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Staff", "Admin" };

    public CreateUserAccountValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid email address.")
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => ValidRoles.Contains(r))
                .WithMessage("Role must be 'Staff' or 'Admin'.");
    }
}

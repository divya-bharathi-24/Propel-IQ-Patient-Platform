using FluentValidation;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateManagedUserCommand"/> (US_045, AC-2).
/// Validation failures produce HTTP 400 via <c>GlobalExceptionFilter</c>.
/// </summary>
public sealed class CreateManagedUserCommandValidator : AbstractValidator<CreateManagedUserCommand>
{
    private static readonly HashSet<string> ValidRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Staff", "Admin" };

    public CreateManagedUserCommandValidator()
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

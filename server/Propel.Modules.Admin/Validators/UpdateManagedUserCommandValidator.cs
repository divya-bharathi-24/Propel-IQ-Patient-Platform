using FluentValidation;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="UpdateManagedUserCommand"/> (US_045).
/// At least one of Name or Role must be provided. Role must be 'Staff' or 'Admin'.
/// Validation failures produce HTTP 400 via <c>GlobalExceptionFilter</c>.
/// </summary>
public sealed class UpdateManagedUserCommandValidator : AbstractValidator<UpdateManagedUserCommand>
{
    private static readonly HashSet<string> ValidRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Staff", "Admin" };

    public UpdateManagedUserCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Name is not null || x.Role is not null)
            .WithMessage("At least one of 'name' or 'role' must be provided.")
            .OverridePropertyName("body");

        When(x => x.Name is not null, () =>
        {
            RuleFor(x => x.Name!)
                .NotEmpty().WithMessage("Name must not be empty.")
                .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        });

        When(x => x.Role is not null, () =>
        {
            RuleFor(x => x.Role!)
                .Must(r => ValidRoles.Contains(r))
                .WithMessage("Role must be 'Staff' or 'Admin'.");
        });
    }
}

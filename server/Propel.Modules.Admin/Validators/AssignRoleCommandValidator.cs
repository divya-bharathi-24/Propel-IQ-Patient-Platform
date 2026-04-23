using FluentValidation;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="AssignRoleCommand"/> (US_046, AC-1, AC-2).
/// <list type="bullet">
///   <item><c>NewRole</c> must be <c>Staff</c> or <c>Admin</c> (Patient accounts are not admin-managed).</item>
///   <item>When <c>NewRole</c> is <c>Admin</c>, <c>ReAuthToken</c> must be present (re-auth gate).</item>
/// </list>
/// Validation failures produce HTTP 400 via <c>GlobalExceptionFilter</c>.
/// </summary>
public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    private static readonly HashSet<string> ValidRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Staff", "Admin" };

    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.NewRole)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => ValidRoles.Contains(r))
                .WithMessage("Role must be 'Staff' or 'Admin'.");

        // ReAuthToken is structurally required when elevating to Admin.
        // Token validity (expiry / single-use) is enforced at the handler level.
        RuleFor(x => x.ReAuthToken)
            .NotEmpty()
            .WithMessage("A valid reAuthToken is required when assigning the Admin role.")
            .When(x => string.Equals(x.NewRole, "Admin", StringComparison.OrdinalIgnoreCase));
    }
}

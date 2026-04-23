using FluentValidation;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="ReauthenticateCommand"/> (US_046, AC-3).
/// Validation failures produce HTTP 400 via <c>GlobalExceptionFilter</c>.
/// </summary>
public sealed class ReauthenticateCommandValidator : AbstractValidator<ReauthenticateCommand>
{
    public ReauthenticateCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.")
            .MaximumLength(200).WithMessage("Current password must not exceed 200 characters.");
    }
}

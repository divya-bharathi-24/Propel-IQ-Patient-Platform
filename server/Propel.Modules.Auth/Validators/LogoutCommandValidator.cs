using FluentValidation;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Validators;

/// <summary>
/// FluentValidation validator for <see cref="LogoutCommand"/>.
/// Logout is a cleanup operation, so validation is lenient: DeviceId and RefreshToken
/// are NOT required to be non-empty (client state may already be cleared).
/// The handler will gracefully skip cleanup steps when these values are empty.
/// </summary>
public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        // DeviceId and RefreshToken are intentionally NOT required for logout.
        // The client may have already cleared its state before calling logout.

        RuleFor(x => x.DeviceId)
            .MaximumLength(256).WithMessage("DeviceId must not exceed 256 characters.")
            .When(x => x.DeviceId is not null);

        RuleFor(x => x.RefreshToken)
            .MaximumLength(512).WithMessage("RefreshToken must not exceed 512 characters.")
            .When(x => x.RefreshToken is not null);

        // UserId is always required (populated from JWT claims by the controller)
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");
    }
}

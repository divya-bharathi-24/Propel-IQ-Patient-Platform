using FluentValidation;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Notification.Validators;

public sealed class PingNotificationCommandValidator : AbstractValidator<PingNotificationCommand>
{
    public PingNotificationCommandValidator()
    {
        // PingNotificationCommand has no properties to validate.
    }
}

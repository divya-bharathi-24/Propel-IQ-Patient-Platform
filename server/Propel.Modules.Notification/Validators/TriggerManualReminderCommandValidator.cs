using FluentValidation;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Notification.Validators;

/// <summary>
/// FluentValidation rules for <see cref="TriggerManualReminderCommand"/> (US_034, AC-1, AC-2).
/// <list type="bullet">
///   <item><c>AppointmentId</c>: must not be an empty GUID.</item>
///   <item><c>StaffUserId</c>: must not be an empty GUID (resolved from JWT; never from body — OWASP A01).</item>
/// </list>
/// Business-rule checks (appointment exists, not cancelled, debounce) are performed in the
/// handler and mapped to HTTP 404 / 422 / 429 respectively — they are NOT validator concerns
/// and do not belong here (would incorrectly produce HTTP 400).
/// Registered automatically by <c>AddValidatorsFromAssemblies</c> in <c>Program.cs</c>.
/// The MediatR <c>ValidationBehavior</c> pipeline executes this validator before the handler.
/// </summary>
public sealed class TriggerManualReminderCommandValidator
    : AbstractValidator<TriggerManualReminderCommand>
{
    public TriggerManualReminderCommandValidator()
    {
        RuleFor(c => c.AppointmentId)
            .NotEmpty()
            .WithMessage("AppointmentId must be a valid non-empty GUID.");

        RuleFor(c => c.StaffUserId)
            .NotEmpty()
            .WithMessage("StaffUserId must be a valid non-empty GUID.");
    }
}

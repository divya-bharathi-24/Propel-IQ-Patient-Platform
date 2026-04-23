using FluentValidation;
using Propel.Modules.Appointment.Commands;

namespace Propel.Modules.Appointment.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CancelWaitlistPreferenceCommand"/> (US_023, AC-4).
/// Ensures <c>WaitlistEntryId</c> is a non-empty GUID before the handler executes.
/// <c>PatientId</c> is validated in the handler via ownership check — not here (OWASP A01).
/// </summary>
public sealed class CancelWaitlistPreferenceValidator
    : AbstractValidator<CancelWaitlistPreferenceCommand>
{
    public CancelWaitlistPreferenceValidator()
    {
        RuleFor(x => x.WaitlistEntryId)
            .NotEmpty()
            .WithMessage("'WaitlistEntryId' must not be empty.");
    }
}

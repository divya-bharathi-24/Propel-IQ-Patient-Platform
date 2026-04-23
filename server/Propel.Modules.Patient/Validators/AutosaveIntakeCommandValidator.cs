using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="AutosaveIntakeCommand"/> (US_029).
/// <para>
/// Only enforces structural integrity (<c>AppointmentId</c> must be present). The four JSONB
/// section payloads are optional — autosave deliberately accepts partial data so the patient
/// can save progress at any point during form entry.
/// </para>
/// </summary>
public sealed class AutosaveIntakeCommandValidator : AbstractValidator<AutosaveIntakeCommand>
{
    public AutosaveIntakeCommandValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("appointmentId must not be empty.");

        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("patientId must be resolvable from the JWT token.");
    }
}

using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="IntakeSessionResumeCommand"/> (US_030, AC-2).
/// <para>
/// Enforces structural integrity only: <c>AppointmentId</c> must be present and
/// <c>PatientId</c> must be resolvable. The four JSONB section payloads inside
/// <c>ExistingFields</c> are optional — the resume endpoint accepts partial data.
/// </para>
/// </summary>
public sealed class IntakeSessionResumeCommandValidator : AbstractValidator<IntakeSessionResumeCommand>
{
    public IntakeSessionResumeCommandValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("appointmentId must not be empty.");

        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("patientId must be resolvable from the JWT token.");

        RuleFor(x => x.ExistingFields)
            .NotNull().WithMessage("existingFields must not be null.");
    }
}

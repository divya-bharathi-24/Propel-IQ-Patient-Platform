using System.Text.Json;
using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="SubmitIntakeCommand"/> (US_029, AC-3, AC-4).
/// <para>
/// Validates structural integrity only (IDs not empty). Semantic field validation
/// (Demographics required fields, Symptoms non-empty) is performed manually inside
/// <see cref="Handlers.SubmitIntakeCommandHandler"/> so that field-level errors can be
/// returned as HTTP 422 (not the pipeline-default HTTP 400) and the partial draft can be
/// persisted before throwing (following the same AC-3 pattern as US_017).
/// </para>
/// </summary>
public sealed class SubmitIntakeCommandValidator : AbstractValidator<SubmitIntakeCommand>
{
    public SubmitIntakeCommandValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("appointmentId must not be empty.");

        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("patientId must be resolvable from the JWT token.");
    }
}

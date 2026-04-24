using FluentValidation;
using Propel.Domain.Interfaces;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Validators;

/// <summary>
/// FluentValidation validator for <see cref="VerifyPatientProfileCommand"/> (AC-3, AC-4).
/// <list type="bullet">
///   <item><c>PatientId</c> must be a non-empty GUID.</item>
///   <item>Patient must exist in the repository (returns 400 Bad Request via FluentValidation pipeline if not).</item>
/// </list>
/// The conflict gate check (AC-4) is performed in the handler to allow structured 409 response
/// with conflict details rather than a generic validation error.
/// </summary>
public sealed class VerifyPatientProfileCommandValidator : AbstractValidator<VerifyPatientProfileCommand>
{
    public VerifyPatientProfileCommandValidator(IPatientRepository patientRepository)
    {
        RuleFor(c => c.PatientId)
            .NotEmpty()
            .WithMessage("PatientId must be a valid non-empty GUID.");

        RuleFor(c => c.StaffUserId)
            .NotEmpty()
            .WithMessage("StaffUserId must be a valid non-empty GUID.");

        RuleFor(c => c.PatientId)
            .MustAsync(async (patientId, ct) =>
                await patientRepository.GetByIdAsync(patientId, ct) is not null)
            .WithMessage("Patient not found.")
            .When(c => c.PatientId != Guid.Empty);
    }
}

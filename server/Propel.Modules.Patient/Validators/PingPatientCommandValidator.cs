using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

public sealed class PingPatientCommandValidator : AbstractValidator<PingPatientCommand>
{
    public PingPatientCommandValidator()
    {
        // PingPatientCommand has no properties to validate.
    }
}

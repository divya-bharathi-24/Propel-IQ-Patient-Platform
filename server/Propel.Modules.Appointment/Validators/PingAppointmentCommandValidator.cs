using FluentValidation;
using Propel.Modules.Appointment.Commands;

namespace Propel.Modules.Appointment.Validators;

public sealed class PingAppointmentCommandValidator : AbstractValidator<PingAppointmentCommand>
{
    public PingAppointmentCommandValidator()
    {
        // PingAppointmentCommand has no properties to validate.
    }
}

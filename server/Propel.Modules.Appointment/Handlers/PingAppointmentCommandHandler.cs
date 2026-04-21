using MediatR;
using Propel.Modules.Appointment.Commands;

namespace Propel.Modules.Appointment.Handlers;

public sealed class PingAppointmentCommandHandler : IRequestHandler<PingAppointmentCommand, string>
{
    public Task<string> Handle(PingAppointmentCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Appointment module OK");
}

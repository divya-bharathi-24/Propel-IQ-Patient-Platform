using MediatR;

namespace Propel.Modules.Appointment.Commands;

public sealed record PingAppointmentCommand : IRequest<string>;

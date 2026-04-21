using MediatR;

namespace Propel.Modules.Patient.Commands;

public sealed record PingPatientCommand : IRequest<string>;

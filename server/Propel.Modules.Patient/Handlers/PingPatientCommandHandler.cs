using MediatR;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Handlers;

public sealed class PingPatientCommandHandler : IRequestHandler<PingPatientCommand, string>
{
    public Task<string> Handle(PingPatientCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Patient module OK");
}

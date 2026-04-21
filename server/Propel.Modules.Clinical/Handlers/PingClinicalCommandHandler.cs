using MediatR;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Handlers;

public sealed class PingClinicalCommandHandler : IRequestHandler<PingClinicalCommand, string>
{
    public Task<string> Handle(PingClinicalCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Clinical module OK");
}

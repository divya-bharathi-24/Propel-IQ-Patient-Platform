using MediatR;
using Propel.Modules.Auth.Commands;

namespace Propel.Modules.Auth.Handlers;

public sealed class PingAuthCommandHandler : IRequestHandler<PingAuthCommand, string>
{
    public Task<string> Handle(PingAuthCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Auth module OK");
}

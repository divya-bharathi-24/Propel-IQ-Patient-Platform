using MediatR;
using Propel.Modules.AI.Commands;

namespace Propel.Modules.AI.Handlers;

public sealed class PingAICommandHandler : IRequestHandler<PingAICommand, string>
{
    public Task<string> Handle(PingAICommand request, CancellationToken cancellationToken)
        => Task.FromResult("AI module OK");
}

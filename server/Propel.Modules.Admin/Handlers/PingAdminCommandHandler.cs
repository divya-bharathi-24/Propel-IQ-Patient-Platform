using MediatR;
using Propel.Modules.Admin.Commands;

namespace Propel.Modules.Admin.Handlers;

public sealed class PingAdminCommandHandler : IRequestHandler<PingAdminCommand, string>
{
    public Task<string> Handle(PingAdminCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Admin module OK");
}

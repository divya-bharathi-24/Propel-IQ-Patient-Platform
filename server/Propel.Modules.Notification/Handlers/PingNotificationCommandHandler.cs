using MediatR;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Notification.Handlers;

public sealed class PingNotificationCommandHandler : IRequestHandler<PingNotificationCommand, string>
{
    public Task<string> Handle(PingNotificationCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Notification module OK");
}

using MediatR;

namespace Propel.Modules.Notification.Commands;

public sealed record PingNotificationCommand : IRequest<string>;

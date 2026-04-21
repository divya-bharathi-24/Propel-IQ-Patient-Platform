using MediatR;

namespace Propel.Modules.Auth.Commands;

public sealed record PingAuthCommand : IRequest<string>;

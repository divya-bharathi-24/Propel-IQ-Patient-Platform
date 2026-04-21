using MediatR;

namespace Propel.Modules.AI.Commands;

public sealed record PingAICommand : IRequest<string>;

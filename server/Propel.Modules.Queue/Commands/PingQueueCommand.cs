using MediatR;

namespace Propel.Modules.Queue.Commands;

/// <summary>Assembly marker used for MediatR registration in Program.cs (US_027).</summary>
public sealed record PingQueueCommand : IRequest<string>;

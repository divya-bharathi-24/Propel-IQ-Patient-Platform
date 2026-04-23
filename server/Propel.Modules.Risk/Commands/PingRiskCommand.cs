using MediatR;

namespace Propel.Modules.Risk.Commands;

/// <summary>Assembly marker used for MediatR registration in Program.cs (us_031, task_002).</summary>
public sealed record PingRiskCommand : IRequest<string>;

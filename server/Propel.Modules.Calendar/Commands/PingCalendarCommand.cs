using MediatR;

namespace Propel.Modules.Calendar.Commands;

/// <summary>
/// Ping command to verify the Calendar module MediatR pipeline is wired (assembly registration).
/// </summary>
public sealed record PingCalendarCommand : IRequest<string>;

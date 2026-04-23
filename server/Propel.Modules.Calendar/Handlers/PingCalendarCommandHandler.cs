using MediatR;
using Propel.Modules.Calendar.Commands;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>Handler for <see cref="PingCalendarCommand"/>.</summary>
public sealed class PingCalendarCommandHandler : IRequestHandler<PingCalendarCommand, string>
{
    public Task<string> Handle(PingCalendarCommand request, CancellationToken cancellationToken)
        => Task.FromResult("Calendar module OK");
}

using System.Diagnostics;
using MediatR;
using Propel.Domain.Interfaces;
using Serilog;
using Serilog.Context;

namespace Propel.Api.Gateway.Infrastructure.Behaviors;

/// <summary>
/// MediatR pipeline behavior that logs request entry and exit for every command and query.
/// Pushes <c>CorrelationId</c> and <c>RequestName</c> into Serilog's <see cref="LogContext"/>
/// so all log entries emitted inside handlers — including database and external service calls —
/// carry the correlation ID even in non-HTTP contexts such as background jobs (AC-1, AC-2, TR-019).
/// </summary>
/// <typeparam name="TRequest">The MediatR request (command/query) type.</typeparam>
/// <typeparam name="TResponse">The response type returned by the handler.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public LoggingBehavior(ICorrelationIdAccessor correlationIdAccessor)
    {
        _correlationIdAccessor = correlationIdAccessor;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string correlationId = _correlationIdAccessor.GetCorrelationId();
        string requestName   = typeof(TRequest).Name;

        // Push both properties into LogContext so all Serilog entries written inside
        // the handler — including EF Core and HttpClient calls — inherit the correlation ID.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestName", requestName))
        {
            Log.Information(
                "Handling {RequestName} [CorrelationId={CorrelationId}]",
                requestName, correlationId);

            var sw = Stopwatch.StartNew();
            var response = await next();
            sw.Stop();

            Log.Information(
                "Handled {RequestName} in {DurationMs}ms [CorrelationId={CorrelationId}]",
                requestName, sw.ElapsedMilliseconds, correlationId);

            return response;
        }
    }
}

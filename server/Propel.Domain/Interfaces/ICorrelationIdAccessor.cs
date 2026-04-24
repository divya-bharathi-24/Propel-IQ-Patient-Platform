namespace Propel.Domain.Interfaces;

/// <summary>
/// Provides read-only access to the correlation ID for the current request.
/// Decouples MediatR pipeline behaviors and application-layer services from a direct
/// <c>IHttpContextAccessor</c> dependency, allowing them to remain infrastructure-agnostic (TR-018, AC-2).
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// Returns the correlation ID for the current request, or <c>"no-context"</c> when called
    /// outside an active HTTP request scope (e.g., background jobs).
    /// </summary>
    string GetCorrelationId();
}

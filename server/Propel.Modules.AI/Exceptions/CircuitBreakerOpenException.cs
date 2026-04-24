namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Guardrails.CircuitBreakerFilter"/> when the Redis-backed AI circuit
/// breaker is in the open state, short-circuiting the request before it reaches the provider
/// (AIR-O02, US_050 AC-1).
/// <para>
/// Caught by <see cref="Services.ExtractionOrchestrator"/> to return a manual-fallback
/// <see cref="Models.ExtractionResult"/> with <c>NeedsManualReview = true</c>.
/// </para>
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}

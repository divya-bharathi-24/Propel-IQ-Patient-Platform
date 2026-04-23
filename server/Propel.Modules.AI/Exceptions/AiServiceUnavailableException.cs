namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <c>IAiIntakeService</c> implementations when the underlying AI provider
/// is unavailable (circuit breaker open, timeout, or connection failure).
/// <para>
/// Caught internally by <c>ProcessIntakeTurnCommandHandler</c> — never propagated to
/// <c>GlobalExceptionFilter</c>. The handler returns HTTP 200 with
/// <c>{ isFallback: true, preservedFields: [...] }</c> so the frontend can switch to
/// manual intake mode without showing a 5xx error (AIR-O02, US_028 AC edge case).
/// </para>
/// </summary>
public sealed class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string message) : base(message)
    {
    }

    public AiServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

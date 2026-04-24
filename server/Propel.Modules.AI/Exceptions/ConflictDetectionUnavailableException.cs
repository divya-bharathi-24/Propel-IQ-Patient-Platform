namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.ConflictDetectionOrchestrator"/> when the Polly circuit
/// breaker is open after consecutive OpenAI failures (EP-008-II/us_044, task_001, AIR-O02).
/// <para>
/// The caller should route the patient to manual staff review for conflict resolution.
/// </para>
/// </summary>
public sealed class ConflictDetectionUnavailableException : Exception
{
    public ConflictDetectionUnavailableException(string message) : base(message)
    {
    }

    public ConflictDetectionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

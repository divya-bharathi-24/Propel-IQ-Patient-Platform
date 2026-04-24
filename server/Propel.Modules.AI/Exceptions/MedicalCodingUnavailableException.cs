namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.MedicalCodingOrchestrator"/> when the Polly circuit breaker
/// is open after two consecutive OpenAI tool-call failures (EP-008-II/us_042, task_001, EC-2, AIR-O02).
/// <para>
/// The BE Medical Coding API handler (task_002) catches this exception and triggers a
/// manual-entry fallback notification to the requesting clinician.
/// </para>
/// </summary>
public sealed class MedicalCodingUnavailableException : Exception
{
    public MedicalCodingUnavailableException(string message) : base(message)
    {
    }

    public MedicalCodingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

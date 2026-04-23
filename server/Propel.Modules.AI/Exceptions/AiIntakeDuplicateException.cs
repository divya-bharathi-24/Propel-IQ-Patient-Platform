namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <c>SubmitAiIntakeCommandHandler</c> when an <c>IntakeRecord</c> already exists
/// for the same <c>(patientId, appointmentId)</c> pair, preventing a duplicate submission.
/// Mapped to HTTP 409 Conflict via <c>GlobalExceptionFilter</c> (US_028, AC-4).
/// </summary>
public sealed class AiIntakeDuplicateException : Exception
{
    public AiIntakeDuplicateException(Guid appointmentId)
        : base($"An intake record already exists for appointment '{appointmentId}'. Duplicate AI intake submission rejected.")
    {
    }
}

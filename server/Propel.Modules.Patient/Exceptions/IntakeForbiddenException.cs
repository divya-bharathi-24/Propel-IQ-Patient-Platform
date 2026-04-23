namespace Propel.Modules.Patient.Exceptions;

/// <summary>
/// Thrown when a patient attempts to access an <see cref="Domain.Entities.IntakeRecord"/>
/// that does not belong to their account (US_030, OWASP A01 — Broken Access Control).
/// <para>
/// Maps to HTTP 403 Forbidden via <c>GlobalExceptionFilter</c>. The message is safe to
/// surface to the client — it contains no PHI or internal system identifiers.
/// </para>
/// </summary>
public sealed class IntakeForbiddenException : Exception
{
    public IntakeForbiddenException(Guid appointmentId)
        : base($"Appointment '{appointmentId}' does not belong to the requesting patient.")
    {
    }
}

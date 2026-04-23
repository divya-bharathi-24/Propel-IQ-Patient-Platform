using Propel.Domain.Entities;

namespace Propel.Modules.Appointment.Infrastructure;

/// <summary>
/// Abstraction for generating and delivering a PDF appointment confirmation email to the patient
/// after a successful booking or reschedule (US_019; US_020, AC-3, NFR-018).
/// <para>
/// Implementations use QuestPDF to generate the confirmation PDF and the SendGrid SDK to
/// deliver it within 60 seconds of a committed appointment transaction (AC-3).
/// </para>
/// <para>
/// Per NFR-018 (graceful degradation): a failure in this service must never roll back the
/// appointment transaction. Callers must catch exceptions from <see cref="SendAsync"/> and log
/// a Serilog <c>Warning</c> event rather than rethrowing.
/// </para>
/// </summary>
public interface IAppointmentConfirmationEmailService
{
    /// <summary>
    /// Generates a PDF confirmation document and sends it to the patient's registered email
    /// address via SendGrid. The operation must complete within 60 seconds (AC-3).
    /// </summary>
    /// <param name="appointment">
    /// The newly committed <see cref="Domain.Entities.Appointment"/> record for which a
    /// confirmation email should be generated. Navigation properties
    /// (<c>Patient</c>, <c>Specialty</c>) must be populated by the caller.
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task SendAsync(
        Domain.Entities.Appointment appointment,
        CancellationToken cancellationToken = default);
}

using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Infrastructure.Pdf;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.Services;

/// <summary>
/// Adapter implementation of <see cref="IAppointmentConfirmationEmailService"/> that combines
/// <see cref="IPdfConfirmationService"/> and <see cref="IEmailService"/> to generate and send
/// appointment confirmation PDFs (US_020, US_021, task_003).
/// <para>
/// Used by <see cref="RescheduleAppointmentCommandHandler"/> which requires the single-method
/// interface rather than injecting both services separately.
/// </para>
/// </summary>
public sealed class AppointmentConfirmationEmailServiceAdapter : IAppointmentConfirmationEmailService
{
    private readonly IPdfConfirmationService _pdfService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AppointmentConfirmationEmailServiceAdapter> _logger;

    public AppointmentConfirmationEmailServiceAdapter(
        IPdfConfirmationService pdfService,
        IEmailService emailService,
        ILogger<AppointmentConfirmationEmailServiceAdapter> logger)
    {
        _pdfService = pdfService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task SendAsync(
        Domain.Entities.Appointment appointment,
        CancellationToken cancellationToken = default)
    {
        if (appointment.Patient is null)
        {
            throw new InvalidOperationException(
                "Appointment.Patient navigation property must be populated before calling SendAsync.");
        }

        if (appointment.Specialty is null)
        {
            throw new InvalidOperationException(
                "Appointment.Specialty navigation property must be populated before calling SendAsync.");
        }

        var referenceNumber = $"APT-{appointment.Id.ToString("N")[..8].ToUpperInvariant()}";

        var pdfData = new PdfConfirmationData(
            ReferenceNumber: referenceNumber,
            PatientName: appointment.Patient.Name,
            AppointmentDate: appointment.Date,
            TimeSlotStart: appointment.TimeSlotStart ?? TimeOnly.MinValue,
            TimeSlotEnd: appointment.TimeSlotEnd ?? TimeOnly.MinValue,
            ProviderSpecialty: appointment.Specialty.Name,
            ClinicName: "Propel IQ Clinic"); // TODO: Replace with actual clinic name from config

        var pdfBytes = await _pdfService.GenerateAsync(pdfData, cancellationToken);

        var emailBody = BuildEmailHtml(
            appointment.Patient.Name,
            appointment.Date,
            appointment.TimeSlotStart ?? TimeOnly.MinValue,
            appointment.TimeSlotEnd ?? TimeOnly.MinValue,
            appointment.Specialty.Name,
            referenceNumber);

        await _emailService.SendEmailWithAttachmentAsync(
            toEmail: appointment.Patient.Email,
            subject: $"Your Appointment Confirmation – {referenceNumber}",
            htmlBody: emailBody,
            attachmentBytes: pdfBytes,
            attachmentFileName: $"confirmation-{referenceNumber}.pdf",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "AppointmentConfirmationEmail sent: AppointmentId={AppointmentId} PatientEmail={Email}",
            appointment.Id, appointment.Patient.Email);
    }

    private static string BuildEmailHtml(
        string patientName,
        DateOnly appointmentDate,
        TimeOnly timeSlotStart,
        TimeOnly timeSlotEnd,
        string specialtyName,
        string referenceNumber) =>
        $"""
        <p>Dear {patientName},</p>
        <p>Your appointment has been confirmed. Please find your confirmation details attached.</p>
        <ul>
          <li><strong>Reference:</strong> {referenceNumber}</li>
          <li><strong>Date:</strong> {appointmentDate:dddd, MMMM d, yyyy}</li>
          <li><strong>Time:</strong> {timeSlotStart:h:mm tt} – {timeSlotEnd:h:mm tt}</li>
          <li><strong>Specialty:</strong> {specialtyName}</li>
          <li><strong>Clinic:</strong> Propel IQ Clinic</li>
        </ul>
        <p>Please arrive 10 minutes before your appointment time.</p>
        <p>The Propel IQ Team</p>
        """;
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Notification.Exceptions;
using System.Threading.Channels;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// MediatR handler for <see cref="BookingConfirmedEvent"/> (US_021, AC-2, AC-3, AC-4).
/// <list type="number">
///   <item><b>Step 1</b> — INSERTs a <c>Notification</c> record (<c>status = Pending</c>) via
///         <see cref="IDbContextFactory{TContext}"/> (AD-7 non-request-scoped write pattern).</item>
///   <item><b>Step 2</b> — Calls <see cref="IPdfConfirmationService.GenerateAsync"/> to produce
///         the branded PDF confirmation document in-memory.</item>
///   <item><b>Step 3</b> — Calls <see cref="IEmailService.SendEmailWithAttachmentAsync"/> to
///         dispatch the PDF via SendGrid.</item>
///   <item><b>Step 4 (success)</b> — UPDATEs <c>Notification.status = Sent</c>, <c>sentAt = UtcNow</c>;
///         INSERTs an <c>AppointmentConfirmationEmailSent</c> audit log entry.</item>
///   <item><b>Step 5 (failure)</b> — On <see cref="EmailDeliveryException"/> or any PDF generation
///         exception: UPDATEs <c>Notification.status = Failed</c>, <c>retryCount = 1</c>,
///         <c>lastRetryAt = UtcNow</c>; writes a <see cref="ConfirmationRetryRequest"/> to the
///         in-process <see cref="Channel{T}"/> for <c>PdfConfirmationRetryService</c>.
///         Exceptions are swallowed — failures must never block the HTTP booking response (AG-6, NFR-018).</item>
/// </list>
/// </summary>
public sealed class BookingConfirmedEventHandler : INotificationHandler<BookingConfirmedEvent>
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IPdfConfirmationService _pdfService;
    private readonly IEmailService _emailService;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly Channel<ConfirmationRetryRequest> _retryChannel;
    private readonly ILogger<BookingConfirmedEventHandler> _logger;

    public BookingConfirmedEventHandler(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IPdfConfirmationService pdfService,
        IEmailService emailService,
        IAuditLogRepository auditLogRepo,
        Channel<ConfirmationRetryRequest> retryChannel,
        ILogger<BookingConfirmedEventHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _pdfService = pdfService;
        _emailService = emailService;
        _auditLogRepo = auditLogRepo;
        _retryChannel = retryChannel;
        _logger = logger;
    }

    public async Task Handle(BookingConfirmedEvent notification, CancellationToken cancellationToken)
    {
        // Step 1 — INSERT Notification record (status = Pending, retryCount = 0).
        // Uses IDbContextFactory for a non-request-scoped write (AD-7 pattern).
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var notificationRecord = new Notification
        {
            Id = Guid.NewGuid(),
            PatientId = notification.PatientId,
            AppointmentId = notification.AppointmentId,
            Channel = NotificationChannel.Email,
            TemplateType = "BookingConfirmation",
            Status = NotificationStatus.Pending,
            RetryCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Notifications.Add(notificationRecord);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "BookingConfirmation Notification created (Pending): NotificationId={NotificationId} " +
            "AppointmentId={AppointmentId} PatientId={PatientId}",
            notificationRecord.Id, notification.AppointmentId, notification.PatientId);

        // Steps 2–3 — Generate PDF then dispatch email.
        // Any failure here must not throw — degrade gracefully per AG-6/NFR-018.
        try
        {
            // Step 2 — Generate PDF in-memory.
            var pdfData = new PdfConfirmationData(
                ReferenceNumber: notification.ReferenceNumber,
                PatientName: notification.PatientName,
                AppointmentDate: notification.AppointmentDate,
                TimeSlotStart: notification.TimeSlotStart,
                TimeSlotEnd: notification.TimeSlotEnd,
                ProviderSpecialty: notification.SpecialtyName,
                ClinicName: notification.ClinicName);

            var pdfBytes = await _pdfService.GenerateAsync(pdfData, cancellationToken);

            // Step 3 — Send PDF via SendGrid.
            await _emailService.SendEmailWithAttachmentAsync(
                toEmail: notification.PatientEmail,
                subject: $"Your Appointment Confirmation – {notification.ReferenceNumber}",
                htmlBody: BuildEmailHtml(notification),
                attachmentBytes: pdfBytes,
                attachmentFileName: $"confirmation-{notification.ReferenceNumber}.pdf",
                cancellationToken: cancellationToken);

            // Step 4 — Update Notification: status = Sent.
            notificationRecord.Status = NotificationStatus.Sent;
            notificationRecord.SentAt = DateTime.UtcNow;
            notificationRecord.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            // Step 4 — Append audit log entry.
            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = notification.PatientId,
                PatientId = notification.PatientId,
                Role = "Patient",
                Action = "AppointmentConfirmationEmailSent",
                EntityType = nameof(Notification),
                EntityId = notificationRecord.Id,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation(
                "BookingConfirmation email sent: NotificationId={NotificationId} " +
                "AppointmentId={AppointmentId} PatientEmail={PatientEmail}",
                notificationRecord.Id, notification.AppointmentId, notification.PatientEmail);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — application is shutting down.
            throw;
        }
        catch (Exception ex)
        {
            // Step 5 — Delivery failed (PDF generation error or SendGrid non-2xx response).
            // Update Notification: status = Failed, retryCount = 1, lastRetryAt = UtcNow.
            // Write retry request to Channel<T> and log Warning — do NOT rethrow (AG-6, NFR-018).
            _logger.LogWarning(ex,
                "BookingConfirmation delivery failed on first attempt: " +
                "NotificationId={NotificationId} AppointmentId={AppointmentId}. " +
                "Writing to retry channel.",
                notificationRecord.Id, notification.AppointmentId);

            try
            {
                notificationRecord.Status = NotificationStatus.Failed;
                notificationRecord.RetryCount = 1;
                notificationRecord.LastRetryAt = DateTime.UtcNow;
                notificationRecord.ErrorMessage = ex.Message.Length > 500
                    ? ex.Message[..500]
                    : ex.Message;
                notificationRecord.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx,
                    "Failed to update Notification status to Failed: NotificationId={NotificationId}",
                    notificationRecord.Id);
            }

            // Write to the retry channel — PdfConfirmationRetryService will pick it up.
            await _retryChannel.Writer.WriteAsync(
                new ConfirmationRetryRequest(
                    NotificationId: notificationRecord.Id,
                    Event: notification,
                    FailedAt: DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private static string BuildEmailHtml(BookingConfirmedEvent evt) =>
        $"""
        <p>Dear {evt.PatientName},</p>
        <p>Your appointment has been confirmed. Please find your confirmation details attached.</p>
        <ul>
          <li><strong>Reference:</strong> {evt.ReferenceNumber}</li>
          <li><strong>Date:</strong> {evt.AppointmentDate:dddd, MMMM d, yyyy}</li>
          <li><strong>Time:</strong> {evt.TimeSlotStart:h:mm tt} – {evt.TimeSlotEnd:h:mm tt}</li>
          <li><strong>Specialty:</strong> {evt.SpecialtyName}</li>
          <li><strong>Clinic:</strong> {evt.ClinicName}</li>
        </ul>
        <p>Please arrive 10 minutes before your appointment time.</p>
        <p>The Propel IQ Team</p>
        """;
}

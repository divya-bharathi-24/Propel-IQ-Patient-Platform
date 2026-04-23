using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using System.Threading.Channels;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Long-running <see cref="BackgroundService"/> that consumes the in-process
/// <see cref="Channel{T}">Channel&lt;ConfirmationRetryRequest&gt;</see> and retries PDF
/// generation plus SendGrid dispatch for failed booking confirmation emails (US_021, AC-4).
/// <para>
/// <b>Retry window:</b> waits until <c>FailedAt + 120 s</c> before processing each item,
/// giving external services time to recover before the single automatic retry fires.
/// </para>
/// <para>
/// <b>On second failure:</b> UPDATEs <c>Notification.retryCount = 2</c> and
/// <c>status = Failed</c>; logs a Serilog <c>Error</c> with the appointment ID.
/// The <c>GET /api/patient/dashboard</c> handler surfaces this record as
/// <c>hasEmailDeliveryFailure = true</c> (US_016 extension).
/// </para>
/// <para>
/// Uses <see cref="IDbContextFactory{TContext}"/> for all database writes (AD-7 pattern,
/// non-request-scoped context). Scoped services (<see cref="IPdfConfirmationService"/>,
/// <see cref="IAuditLogRepository"/>) are resolved via <see cref="IServiceScopeFactory"/>
/// per retry item.
/// </para>
/// </summary>
public sealed class PdfConfirmationRetryService : BackgroundService
{
    private readonly Channel<ConfirmationRetryRequest> _retryChannel;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PdfConfirmationRetryService> _logger;

    public PdfConfirmationRetryService(
        Channel<ConfirmationRetryRequest> retryChannel,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<PdfConfirmationRetryService> logger)
    {
        _retryChannel = retryChannel;
        _dbContextFactory = dbContextFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _retryChannel.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessRetryAsync(request, stoppingToken);
        }
    }

    private async Task ProcessRetryAsync(ConfirmationRetryRequest request, CancellationToken stoppingToken)
    {
        // Respect 2-minute retry window from the time of first failure (AC-4).
        var retryNotBefore = request.FailedAt.AddSeconds(120);
        var waitMs = (int)(retryNotBefore - DateTimeOffset.UtcNow).TotalMilliseconds;
        if (waitMs > 0)
        {
            _logger.LogDebug(
                "PdfConfirmationRetry: waiting {WaitMs}ms before retry. " +
                "NotificationId={NotificationId} AppointmentId={AppointmentId}",
                waitMs, request.NotificationId, request.Event.AppointmentId);

            await Task.Delay(waitMs, stoppingToken);
        }

        _logger.LogInformation(
            "PdfConfirmationRetry: starting retry attempt. " +
            "NotificationId={NotificationId} AppointmentId={AppointmentId}",
            request.NotificationId, request.Event.AppointmentId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        var notificationRecord = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId, stoppingToken);

        if (notificationRecord is null)
        {
            _logger.LogError(
                "PdfConfirmationRetry: Notification record not found. " +
                "NotificationId={NotificationId} — skipping retry.",
                request.NotificationId);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var pdfService = scope.ServiceProvider.GetRequiredService<IPdfConfirmationService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var auditLogRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        try
        {
            // Retry — PDF generation.
            var pdfData = new PdfConfirmationData(
                ReferenceNumber: request.Event.ReferenceNumber,
                PatientName: request.Event.PatientName,
                AppointmentDate: request.Event.AppointmentDate,
                TimeSlotStart: request.Event.TimeSlotStart,
                TimeSlotEnd: request.Event.TimeSlotEnd,
                ProviderSpecialty: request.Event.SpecialtyName,
                ClinicName: request.Event.ClinicName);

            var pdfBytes = await pdfService.GenerateAsync(pdfData, stoppingToken);

            // Retry — SendGrid dispatch.
            await emailService.SendEmailWithAttachmentAsync(
                toEmail: request.Event.PatientEmail,
                subject: $"Your Appointment Confirmation – {request.Event.ReferenceNumber}",
                htmlBody: BuildEmailHtml(request.Event),
                attachmentBytes: pdfBytes,
                attachmentFileName: $"confirmation-{request.Event.ReferenceNumber}.pdf",
                cancellationToken: stoppingToken);

            // Retry succeeded — UPDATE Notification: status = Sent.
            notificationRecord.Status = NotificationStatus.Sent;
            notificationRecord.SentAt = DateTime.UtcNow;
            notificationRecord.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);

            // Append success audit log.
            await auditLogRepo.AppendAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = request.Event.PatientId,
                PatientId = request.Event.PatientId,
                Role = "Patient",
                Action = "AppointmentConfirmationEmailSent",
                EntityType = nameof(Notification),
                EntityId = request.NotificationId,
                Timestamp = DateTime.UtcNow
            }, stoppingToken);

            _logger.LogInformation(
                "PdfConfirmationRetry: succeeded on retry. " +
                "NotificationId={NotificationId} AppointmentId={AppointmentId}",
                request.NotificationId, request.Event.AppointmentId);
        }
        catch (OperationCanceledException)
        {
            // Application is shutting down — do not mark as double-failed.
            throw;
        }
        catch (Exception ex)
        {
            // Second failure — UPDATE retryCount = 2 and log Error.
            // The dashboard query will surface hasEmailDeliveryFailure = true (US_016).
            _logger.LogError(ex,
                "PdfConfirmationRetry: retry attempt failed (second failure). " +
                "Patient will be notified via dashboard alert. " +
                "NotificationId={NotificationId} AppointmentId={AppointmentId}",
                request.NotificationId, request.Event.AppointmentId);

            try
            {
                notificationRecord.Status = NotificationStatus.Failed;
                notificationRecord.RetryCount = 2;
                notificationRecord.LastRetryAt = DateTime.UtcNow;
                notificationRecord.ErrorMessage = ex.Message.Length > 500
                    ? ex.Message[..500]
                    : ex.Message;
                notificationRecord.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx,
                    "PdfConfirmationRetry: failed to persist double-failure status. " +
                    "NotificationId={NotificationId}",
                    request.NotificationId);
            }
        }
    }

    private static string BuildEmailHtml(Propel.Modules.Appointment.Events.BookingConfirmedEvent evt) =>
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

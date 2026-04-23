using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Appointment.Exceptions;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="CancelAppointmentCommand"/> for <c>POST /api/appointments/{id}/cancel</c>
/// (US_020, AC-1, AC-2, AC-4).
/// <list type="number">
///   <item><b>Step 1 — Load appointment with related data</b> via eager-loaded query (Notifications, WaitlistEntry, CalendarSync).</item>
///   <item><b>Step 2 — Ownership check</b>: <c>appointment.PatientId != command.PatientId</c> → <see cref="ForbiddenAccessException"/> → HTTP 403 (OWASP A01).</item>
///   <item><b>Step 3 — Future-date check</b>: <c>appointment.Date &lt; today UTC</c> → <see cref="BusinessRuleViolationException"/> → HTTP 400.</item>
///   <item><b>Step 4 — Mutate appointment</b>: <c>Status = Cancelled</c>, <c>CancellationReason</c> set.</item>
///   <item><b>Step 5 — Suppress notifications</b>: all Pending <see cref="Notification"/> records set to <c>Cancelled</c>.</item>
///   <item><b>Step 6 — Cancel waitlist entry</b>: Active <see cref="WaitlistEntry"/> set to <c>Cancelled</c> (AC-4).</item>
///   <item><b>Step 7 — Atomic commit</b>: single <c>SaveChangesAsync()</c> for all mutations above.</item>
///   <item><b>Step 7b — Publish <see cref="AppointmentCancelledEvent"/></b>: triggers FIFO waitlist resolution via <c>SlotReleasedEventHandler</c> (US_024, AC-1).</item>
///   <item><b>Step 8 — Cache invalidation</b>: <see cref="ISlotCacheService.InvalidateAsync"/> (non-transactional; failure is non-blocking).</item>
///   <item><b>Step 9 — Queue calendar revocation</b>: fire-and-forget via <see cref="ICalendarSyncRevocationService"/> (AC-2, NFR-018).</item>
///   <item><b>Step 10 — Audit log</b>: immutable <see cref="AuditLog"/> entry via <see cref="IAuditLogRepository"/> (AD-7).</item>
/// </list>
/// </summary>
public sealed class CancelAppointmentCommandHandler
    : IRequestHandler<CancelAppointmentCommand, Unit>
{
    private readonly IAppointmentBookingRepository _bookingRepo;
    private readonly ISlotCacheService _slotCache;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ICalendarSyncRevocationService _calendarSyncRevocation;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CancelAppointmentCommandHandler> _logger;

    public CancelAppointmentCommandHandler(
        IAppointmentBookingRepository bookingRepo,
        ISlotCacheService slotCache,
        IAuditLogRepository auditLogRepo,
        ICalendarSyncRevocationService calendarSyncRevocation,
        IPublisher publisher,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CancelAppointmentCommandHandler> logger)
    {
        _bookingRepo = bookingRepo;
        _slotCache = slotCache;
        _auditLogRepo = auditLogRepo;
        _calendarSyncRevocation = calendarSyncRevocation;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Unit> Handle(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();

        // Step 1 — Load appointment with related entities (Notifications, WaitlistEntry, CalendarSync).
        var appointment = await _bookingRepo.GetByIdWithRelatedAsync(
            command.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "CancelAppointment_NotFound: AppointmentId={AppointmentId} PatientId={PatientId}",
                command.AppointmentId, command.PatientId);
            throw new KeyNotFoundException($"Appointment '{command.AppointmentId}' was not found.");
        }

        // Step 2 — Ownership check: patient may only cancel their own appointment (OWASP A01).
        if (appointment.PatientId != command.PatientId)
        {
            _logger.LogWarning(
                "CancelAppointment_Forbidden: AppointmentId={AppointmentId} RequestingPatientId={PatientId} OwnerPatientId={OwnerId}",
                command.AppointmentId, command.PatientId, appointment.PatientId);
            throw new ForbiddenAccessException(
                "You are not authorised to cancel this appointment.");
        }

        // Step 3 — Future-date check: past appointments cannot be cancelled (AC-1 edge case).
        if (appointment.Date < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            _logger.LogWarning(
                "CancelAppointment_PastDate: AppointmentId={AppointmentId} AppointmentDate={Date}",
                command.AppointmentId, appointment.Date);
            throw new BusinessRuleViolationException("Cannot cancel a past appointment");
        }

        // Step 4 — Mutate appointment: set status = Cancelled and record the reason.
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = command.CancellationReason;

        // Step 5 — Suppress all Pending notification records for this appointment (AC-2).
        foreach (var notification in appointment.Notifications)
        {
            notification.Status = NotificationStatus.Cancelled;
        }

        // Step 6 — Cancel the active waitlist entry linked to this appointment (AC-4).
        if (appointment.WaitlistEntry is not null
            && appointment.WaitlistEntry.Status == WaitlistStatus.Active)
        {
            appointment.WaitlistEntry.Status = WaitlistStatus.Cancelled;
        }

        // Step 7 — Atomic commit: appointment + notifications + waitlist in one transaction.
        await _bookingRepo.SaveAsync(cancellationToken);

        _logger.LogInformation(
            "AppointmentCancelled: AppointmentId={AppointmentId} PatientId={PatientId}",
            appointment.Id, command.PatientId);

        // Step 7b — Publish AppointmentCancelledEvent to trigger FIFO waitlist resolution (US_024, AC-1).
        // Only published when the appointment had a time slot — queued-only walk-in appointments
        // (TimeSlotStart = null) have no slot to release for waitlist purposes (US_026, AC-3).
        // Fire-and-forget via Publish (not Send) — failures in SlotReleasedEventHandler must not
        // block the cancellation response (AG-6, NFR-018).
        if (appointment.TimeSlotStart.HasValue && appointment.TimeSlotEnd.HasValue)
        {
            await _publisher.Publish(new AppointmentCancelledEvent(
                CancelledAppointmentId: appointment.Id,
                SpecialtyId:            appointment.SpecialtyId,
                Date:                   appointment.Date,
                TimeSlotStart:          appointment.TimeSlotStart.Value,
                TimeSlotEnd:            appointment.TimeSlotEnd.Value
            ), cancellationToken);
        }

        // Step 8 — Cache invalidation (non-transactional; Redis failure must not block response).
        try
        {
            var specialtyIdStr = appointment.SpecialtyId.ToString();
            await _slotCache.InvalidateAsync(specialtyIdStr, appointment.Date, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CancelAppointment_CacheInvalidationFailed: AppointmentId={AppointmentId}",
                appointment.Id);
        }

        // Step 9 — Queue calendar sync revocation as fire-and-forget (AC-2, NFR-018).
        _calendarSyncRevocation.EnqueueRevoke(appointment.Id);

        // Step 10 — Immutable audit log entry (AD-7).
        var auditDetails = JsonDocument.Parse(
            JsonSerializer.Serialize(new { cancellationReason = command.CancellationReason }));

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.PatientId,
            PatientId = command.PatientId,
            Role = "Patient",
            Action = "AppointmentCancelled",
            EntityType = nameof(Domain.Entities.Appointment),
            EntityId = appointment.Id,
            Details = auditDetails,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Unit.Value;
    }
}

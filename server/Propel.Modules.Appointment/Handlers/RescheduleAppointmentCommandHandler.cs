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
/// Handles <see cref="RescheduleAppointmentCommand"/> for <c>POST /api/appointments/{id}/reschedule</c>
/// (US_020, AC-3, task_003).
/// <list type="number">
///   <item><b>Step 1 — Load original appointment</b> with related entities via eager-load query.</item>
///   <item><b>Step 2 — Ownership check</b>: <c>appointment.PatientId != command.PatientId</c> → <see cref="ForbiddenAccessException"/> → HTTP 403 (OWASP A01).</item>
///   <item><b>Step 3 — Future-date guard</b>: <c>appointment.Date &lt; today UTC</c> → <see cref="BusinessRuleViolationException"/> → HTTP 400.</item>
///   <item><b>Step 4 — Slot pre-check</b>: query new slot for active bookings → <see cref="SlotConflictException"/> → HTTP 409.</item>
///   <item><b>Step 5 — Cancel original</b>: <c>Status = Cancelled</c>, suppress Pending Notifications, cancel Active WaitlistEntry.</item>
///   <item><b>Step 6 — Stage new Appointment</b>: <c>Status = Booked</c>, new GUID, patient / specialty / slot from command.</item>
///   <item><b>Step 7 — Atomic commit</b>: single <c>SaveChangesAsync()</c>; on <see cref="DbUpdateException"/> unique-index violation → <see cref="SlotConflictException"/> → HTTP 409.</item>
///   <item><b>Step 8 — Cache invalidation</b> (non-blocking): original slot and new slot keys deleted from Redis.</item>
///   <item><b>Step 9 — Queue calendar revocation</b>: fire-and-forget for original appointment via <see cref="ICalendarSyncRevocationService"/> (AC-2, NFR-018).</item>
///   <item><b>Step 9b — Publish <see cref="AppointmentRescheduledEvent"/></b>: triggers <c>CalendarUpdateOnRescheduleHandler</c> for asynchronous calendar PATCH of new appointment (US_037, AC-1).</item>
///   <item><b>Step 10 — PDF confirmation email</b>: delivered within 60 s via <see cref="IAppointmentConfirmationEmailService"/>; failure logged as Warning — never rethrown (NFR-018).</item>
///   <item><b>Step 11 — Audit log INSERT</b>: immutable <see cref="AuditLog"/> entry via <see cref="IAuditLogRepository"/> (AD-7).</item>
/// </list>
/// </summary>
public sealed class RescheduleAppointmentCommandHandler
    : IRequestHandler<RescheduleAppointmentCommand, RescheduleAppointmentResult>
{
    private readonly IAppointmentBookingRepository _bookingRepo;
    private readonly ISlotCacheService _slotCache;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ICalendarSyncRevocationService _calendarSyncRevocation;
    private readonly IAppointmentConfirmationEmailService _confirmationEmailService;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RescheduleAppointmentCommandHandler> _logger;

    public RescheduleAppointmentCommandHandler(
        IAppointmentBookingRepository bookingRepo,
        ISlotCacheService slotCache,
        IAuditLogRepository auditLogRepo,
        ICalendarSyncRevocationService calendarSyncRevocation,
        IAppointmentConfirmationEmailService confirmationEmailService,
        IPublisher publisher,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RescheduleAppointmentCommandHandler> logger)
    {
        _bookingRepo = bookingRepo;
        _slotCache = slotCache;
        _auditLogRepo = auditLogRepo;
        _calendarSyncRevocation = calendarSyncRevocation;
        _confirmationEmailService = confirmationEmailService;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<RescheduleAppointmentResult> Handle(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();

        // Step 1 — Load original appointment with related entities (Notifications, WaitlistEntry, CalendarSync).
        var appointment = await _bookingRepo.GetByIdWithRelatedAsync(
            command.OriginalAppointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "RescheduleAppointment_NotFound: AppointmentId={AppointmentId} PatientId={PatientId}",
                command.OriginalAppointmentId, command.PatientId);
            throw new KeyNotFoundException(
                $"Appointment '{command.OriginalAppointmentId}' was not found.");
        }

        // Step 2 — Ownership check: patient may only reschedule their own appointment (OWASP A01).
        if (appointment.PatientId != command.PatientId)
        {
            _logger.LogWarning(
                "RescheduleAppointment_Forbidden: AppointmentId={AppointmentId} RequestingPatientId={PatientId} OwnerPatientId={OwnerId}",
                command.OriginalAppointmentId, command.PatientId, appointment.PatientId);
            throw new ForbiddenAccessException(
                "You are not authorised to reschedule this appointment.");
        }

        // Step 3 — Future-date guard: past appointments cannot be rescheduled (AC-3 edge case).
        if (appointment.Date < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            _logger.LogWarning(
                "RescheduleAppointment_PastDate: AppointmentId={AppointmentId} AppointmentDate={Date}",
                command.OriginalAppointmentId, appointment.Date);
            throw new BusinessRuleViolationException("Cannot reschedule a past appointment");
        }

        // Step 4 — Optimistic slot pre-check: detect taken slot before mutation (US_020, AC-3).
        var slotTaken = await _bookingRepo.IsSlotTakenAsync(
            command.SpecialtyId,
            command.NewDate,
            command.NewTimeSlotStart,
            cancellationToken);

        if (slotTaken)
        {
            _logger.LogWarning(
                "RescheduleAppointment_SlotConflict (pre-check): SpecialtyId={SpecialtyId} Date={Date} TimeSlot={TimeSlot} PatientId={PatientId}",
                command.SpecialtyId, command.NewDate, command.NewTimeSlotStart, command.PatientId);
            throw new SlotConflictException("Slot no longer available");
        }

        // Capture original slot identifiers before mutation for post-commit cache invalidation.
        var originalSpecialtyId = appointment.SpecialtyId.ToString();
        var originalDate = appointment.Date;

        // Step 5 — Cancel original appointment: set status, suppress Pending notifications,
        // and cancel the Active waitlist entry (mirrors CancelAppointmentCommandHandler logic).
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = "Rescheduled by patient";

        foreach (var notification in appointment.Notifications)
        {
            notification.Status = NotificationStatus.Cancelled;
        }

        if (appointment.WaitlistEntry is not null
            && appointment.WaitlistEntry.Status == WaitlistStatus.Active)
        {
            appointment.WaitlistEntry.Status = WaitlistStatus.Cancelled;
        }

        // Step 6 — Stage new Appointment entity for the selected slot (status = Booked).
        var newAppointment = new Domain.Entities.Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = command.PatientId,
            SpecialtyId = command.SpecialtyId,
            Date = command.NewDate,
            TimeSlotStart = command.NewTimeSlotStart,
            TimeSlotEnd = command.NewTimeSlotEnd,
            Status = AppointmentStatus.Booked,
            CreatedBy = command.PatientId,
            CreatedAt = DateTime.UtcNow
        };

        _bookingRepo.StageAppointment(newAppointment);

        // Step 7 — Atomic commit: original cancellation + new booking in one round-trip.
        // The repository's SaveAsync detects unique partial-index violations and throws
        // SlotConflictException so this module layer stays free of EF Core dependencies.
        try
        {
            await _bookingRepo.SaveAsync(cancellationToken);
        }
        catch (SlotConflictException)
        {
            _logger.LogWarning(
                "RescheduleAppointment_SlotConflict (commit): SpecialtyId={SpecialtyId} Date={Date} TimeSlot={TimeSlot} PatientId={PatientId}",
                command.SpecialtyId, command.NewDate, command.NewTimeSlotStart, command.PatientId);
            throw;
        }

        _logger.LogInformation(
            "AppointmentRescheduled: OriginalAppointmentId={OriginalId} NewAppointmentId={NewId} PatientId={PatientId}",
            command.OriginalAppointmentId, newAppointment.Id, command.PatientId);

        // Step 8 — Cache invalidation for both original and new slot (non-blocking; failure must not block response).
        try
        {
            await _slotCache.InvalidateAsync(originalSpecialtyId, originalDate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "RescheduleAppointment_CacheInvalidationFailed (original): AppointmentId={AppointmentId}",
                command.OriginalAppointmentId);
        }

        try
        {
            await _slotCache.InvalidateAsync(
                command.SpecialtyId.ToString(), command.NewDate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "RescheduleAppointment_CacheInvalidationFailed (new): AppointmentId={NewAppointmentId}",
                newAppointment.Id);
        }

        // Step 9 — Queue calendar sync revocation for original appointment as fire-and-forget (AC-2, NFR-018).
        _calendarSyncRevocation.EnqueueRevoke(command.OriginalAppointmentId);

        // Step 9b — Publish AppointmentRescheduledEvent post-commit so CalendarUpdateOnRescheduleHandler
        // can asynchronously PATCH the calendar event for the new appointment if a CalendarSync record
        // exists for it (US_037, AC-1). If none exists, propagation is skipped silently (AC-4).
        await _publisher.Publish(
            new AppointmentRescheduledEvent(
                AppointmentId:    newAppointment.Id,
                NewDate:          command.NewDate,
                NewTimeSlotStart: command.NewTimeSlotStart,
                NewTimeSlotEnd:   command.NewTimeSlotEnd),
            cancellationToken);

        // Step 10 — PDF confirmation email for new appointment (AC-3).
        // Failure must not roll back the transaction — log Warning and continue (NFR-018).
        try
        {
            await _confirmationEmailService.SendAsync(newAppointment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ConfirmationEmail_Failed: NewAppointmentId={NewAppointmentId} PatientId={PatientId}",
                newAppointment.Id, command.PatientId);
        }

        // Step 11 — Immutable audit log entry (AD-7).
        var auditDetails = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                originalAppointmentId = command.OriginalAppointmentId,
                newDate = command.NewDate.ToString("yyyy-MM-dd"),
                newTimeSlot = command.NewTimeSlotStart.ToString("HH:mm")
            }));

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.PatientId,
            PatientId = command.PatientId,
            Role = "Patient",
            Action = "AppointmentRescheduled",
            EntityType = nameof(Domain.Entities.Appointment),
            EntityId = newAppointment.Id,
            Details = auditDetails,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        var confirmationNumber = $"APT-{newAppointment.Id.ToString("N")[..8].ToUpperInvariant()}";

        return new RescheduleAppointmentResult(
            NewAppointmentId: newAppointment.Id,
            ConfirmationNumber: confirmationNumber);
    }

}

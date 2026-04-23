using System.Data;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Appointment.Exceptions;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// MediatR handler for <see cref="ExecuteSlotSwapCommand"/> (US_024, AC-2, AC-3, AC-4).
/// Implements UC004_SWAP + UC004_RELEASE pipeline stages:
/// <list type="number">
///   <item><b>Step 1 — Retry shell</b>: wraps the transaction in a retry loop (max 3 attempts).
///         PostgreSQL deadlock (40P01) triggers exponential back-off: 0 ms → 100 ms → 200 ms.
///         After 3 consecutive deadlocks, logs <c>Error</c> and throws
///         <see cref="SlotSwapPermanentFailureException"/>; WaitlistEntry remains <c>Active</c>.</item>
///   <item><b>Step 2 — Atomic transaction (<see cref="IsolationLevel.ReadCommitted"/>)</b>:
///     <list type="bullet">
///       <item>Cancels the current appointment (<c>status = Cancelled</c>, <c>cancellationReason = "AutoSwap"</c>).</item>
///       <item>INSERTs a new <see cref="Appointment"/> for the preferred slot (<c>status = Booked</c>).</item>
///       <item>Updates the <see cref="WaitlistEntry"/> to <c>status = Swapped</c>.</item>
///       <item><c>SaveChangesAsync</c> enforces the unique partial index
///             <c>IX_appointments_slot_uniqueness</c> (US_019/TASK_003) to prevent double-booking.</item>
///     </list>
///   </item>
///   <item><b>Step 3 — Race condition (AC-4)</b>: unique constraint violation (23505) rolls back
///         the transaction, leaves WaitlistEntry <c>Active</c>, logs <c>Warning</c>, and
///         re-publishes <see cref="AppointmentCancelledEvent"/> to advance to the next FIFO candidate.</item>
///   <item><b>Step 4 — Post-commit</b>:
///     <list type="bullet">
///       <item>Invalidates Redis cache for both affected slot dates via <see cref="ISlotCacheService"/>
///             (AC-3, AD-8, NFR-020 staleness ≤ 5 s).</item>
///       <item>Appends immutable audit log <c>SlotSwapExecuted</c> via <see cref="IAuditLogRepository"/> (AD-7).</item>
///       <item>Publishes <see cref="SlotSwapCompletedEvent"/> for email/SMS notification (FR-023, US_025).</item>
///     </list>
///   </item>
/// </list>
/// Uses <see cref="IDbContextFactory{TContext}"/> for all DB access — this handler is invoked
/// from a MediatR request in a non-request-scoped context (AD-7).
/// </summary>
public sealed class ExecuteSlotSwapCommandHandler : IRequestHandler<ExecuteSlotSwapCommand>
{
    /// <summary>Delay schedule in milliseconds per attempt (0 ms, 100 ms, 200 ms).</summary>
    private static readonly int[] RetryDelaysMs = [0, 100, 200];

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ISlotCacheService _slotCache;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPublisher _publisher;
    private readonly ILogger<ExecuteSlotSwapCommandHandler> _logger;

    public ExecuteSlotSwapCommandHandler(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ISlotCacheService slotCache,
        IAuditLogRepository auditLogRepo,
        IPublisher publisher,
        ILogger<ExecuteSlotSwapCommandHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _slotCache = slotCache;
        _auditLogRepo = auditLogRepo;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(ExecuteSlotSwapCommand request, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (RetryDelaysMs[attempt] > 0)
                await Task.Delay(RetryDelaysMs[attempt], cancellationToken);

            try
            {
                var (newAppointmentId, originalDate) =
                    await ExecuteTransactionAsync(request, cancellationToken);

                await PostCommitAsync(request, newAppointmentId, originalDate, cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (IsDeadlock(ex))
            {
                if (attempt == 2)
                {
                    _logger.LogError(
                        ex,
                        "SlotSwap_DeadlockExhausted: WaitlistEntry {WaitlistEntryId} remains Active after 3 attempts",
                        request.WaitlistEntryId);
                    throw new SlotSwapPermanentFailureException(request.WaitlistEntryId);
                }

                _logger.LogWarning(
                    "SlotSwap_Deadlock: Attempt {Attempt} for WaitlistEntry {WaitlistEntryId}, retrying",
                    attempt + 1,
                    request.WaitlistEntryId);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Race condition: preferred slot was claimed by another patient before this transaction
                // committed. WaitlistEntry remains Active. Re-publish AppointmentCancelledEvent to
                // trigger the next FIFO candidate evaluation (AC-4).
                _logger.LogWarning(
                    "SlotSwap_RaceCondition: Slot {SpecialtyId}/{Date}/{Time} claimed concurrently — WaitlistEntry {WaitlistEntryId} remains Active",
                    request.SpecialtyId,
                    request.PreferredDate,
                    request.PreferredTimeSlotStart,
                    request.WaitlistEntryId);

                await _publisher.Publish(new AppointmentCancelledEvent(
                    CancelledAppointmentId: Guid.Empty,
                    SpecialtyId:            request.SpecialtyId,
                    Date:                   request.PreferredDate,
                    TimeSlotStart:          request.PreferredTimeSlotStart,
                    TimeSlotEnd:            request.PreferredTimeSlotEnd
                ), cancellationToken);

                return;
            }
        }
    }

    /// <summary>
    /// Executes the atomic three-step database transaction:
    /// cancel current appointment → INSERT new appointment → UPDATE WaitlistEntry to Swapped.
    /// </summary>
    /// <returns>The new appointment ID and the original appointment's date (needed for cache invalidation).</returns>
    private async Task<(Guid NewAppointmentId, DateOnly OriginalDate)> ExecuteTransactionAsync(
        ExecuteSlotSwapCommand request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // Cancel the patient's current appointment.
        var currentAppt = await dbContext.Appointments
            .FindAsync([request.CurrentAppointmentId], cancellationToken);

        currentAppt!.Status = AppointmentStatus.Cancelled;
        currentAppt.CancellationReason = "AutoSwap";
        var originalDate = currentAppt.Date;

        // INSERT new appointment for the preferred slot (status = Booked).
        var newAppt = new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = request.PatientId,
            SpecialtyId = request.SpecialtyId,
            Date = request.PreferredDate,
            TimeSlotStart = request.PreferredTimeSlotStart,
            TimeSlotEnd = request.PreferredTimeSlotEnd,
            Status = AppointmentStatus.Booked,
            CreatedBy = request.PatientId,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Appointments.Add(newAppt);

        // Mark WaitlistEntry as Swapped.
        var waitlistEntry = await dbContext.WaitlistEntries
            .FindAsync([request.WaitlistEntryId], cancellationToken);

        waitlistEntry!.Status = WaitlistStatus.Swapped;

        // SaveChangesAsync enforces the unique partial index IX_appointments_slot_uniqueness on
        // (specialty_id, date, time_slot_start) WHERE status != 'Cancelled' (US_019/TASK_003).
        // A concurrent INSERT for the same slot will raise DbUpdateException (23505).
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "SlotSwap_Committed: WaitlistEntry {WaitlistEntryId} swapped; NewAppointmentId={NewAppointmentId}",
            request.WaitlistEntryId,
            newAppt.Id);

        return (newAppt.Id, originalDate);
    }

    /// <summary>
    /// Executes post-commit side effects: Redis cache invalidation, audit log, and
    /// SlotSwapCompletedEvent notification. Failures here are non-transactional;
    /// cache invalidation failures are swallowed (NFR-018).
    /// </summary>
    private async Task PostCommitAsync(
        ExecuteSlotSwapCommand request,
        Guid newAppointmentId,
        DateOnly originalDate,
        CancellationToken cancellationToken)
    {
        var specialtyIdStr = request.SpecialtyId.ToString();

        // Invalidate slot cache for both the preferred slot (now booked) and the original
        // slot (now free) — AC-3, AD-8, NFR-020 staleness ≤ 5 s.
        try
        {
            await _slotCache.InvalidateAsync(specialtyIdStr, request.PreferredDate, cancellationToken);
            await _slotCache.InvalidateAsync(specialtyIdStr, originalDate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SlotSwap_CacheInvalidationFailed: SpecialtyId={SpecialtyId} PreferredDate={PreferredDate} OriginalDate={OriginalDate}",
                request.SpecialtyId,
                request.PreferredDate,
                originalDate);
        }

        // Immutable audit log entry — AD-7, write-only repository (IAuditLogRepository).
        var auditDetails = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            originalAppointmentId = request.CurrentAppointmentId,
            waitlistEntryId = request.WaitlistEntryId,
            patientId = request.PatientId
        }));

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.PatientId,
            PatientId = request.PatientId,
            Role = "Patient",
            Action = "SlotSwapExecuted",
            EntityType = nameof(Appointment),
            EntityId = newAppointmentId,
            Details = auditDetails,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        // FR-023 — Publish swap completion event; consumed by Notification Module for US_025.
        await _publisher.Publish(new SlotSwapCompletedEvent(
            NewAppointmentId:  newAppointmentId,
            PatientId:         request.PatientId,
            SpecialtyId:       request.SpecialtyId,
            NewDate:           request.PreferredDate,
            NewTimeSlotStart:  request.PreferredTimeSlotStart,
            NewTimeSlotEnd:    request.PreferredTimeSlotEnd,
            WaitlistEntryId:   request.WaitlistEntryId
        ), cancellationToken);
    }

    /// <summary>Detects PostgreSQL deadlock (error code 40P01).</summary>
    private static bool IsDeadlock(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("40P01", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Detects PostgreSQL unique constraint violation (error code 23505).</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("23505", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true;
}

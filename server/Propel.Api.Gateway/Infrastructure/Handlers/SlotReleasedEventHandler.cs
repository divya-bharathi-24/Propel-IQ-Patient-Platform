using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Enums;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Events;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// MediatR handler for <see cref="AppointmentCancelledEvent"/> (US_024, AC-1, AC-4).
/// Implements the UC004_DETECT + UC004_MATCH pipeline stages:
/// <list type="number">
///   <item><b>Step 1 — FIFO query</b>: Loads up to <see cref="MaxFifoIterations"/> active
///         <c>WaitlistEntry</c> records for the released slot, ordered by <c>enrolledAt ASC</c>
///         (DR-003). Uses <c>AsNoTracking()</c> for the read-only scan.</item>
///   <item><b>Step 2 — Eligibility loop</b>: For each FIFO candidate, verifies the current
///         appointment is still <c>Booked</c>. Candidates whose current appointment is no longer
///         <c>Booked</c> have their <c>WaitlistEntry.status</c> updated to <c>Expired</c>
///         via <see cref="IDbContextFactory{TContext}"/> (AD-7 pattern) and are skipped.</item>
///   <item><b>Step 3 — Dispatch</b>: Sends <see cref="ExecuteSlotSwapCommand"/> for the first
///         eligible candidate and exits the loop (one swap per cancellation event).</item>
///   <item><b>No candidates</b>: Logs <c>Debug</c> and returns without error.</item>
/// </list>
/// Uses <see cref="IDbContextFactory{TContext}"/> for all DB access — this handler is invoked
/// from a MediatR notification (non-request-scoped context, AD-7).
/// </summary>
public sealed class SlotReleasedEventHandler : INotificationHandler<AppointmentCancelledEvent>
{
    private const int MaxFifoIterations = 5;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IMediator _mediator;
    private readonly ILogger<SlotReleasedEventHandler> _logger;

    public SlotReleasedEventHandler(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IMediator mediator,
        ILogger<SlotReleasedEventHandler> logger)
    {
        _dbContextFactory = dbContextFactory;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(AppointmentCancelledEvent notification, CancellationToken cancellationToken)
    {
        // Step 1 — FIFO read-only scan: load up to MaxFifoIterations candidates ordered by enrolledAt ASC (DR-003).
        await using var readCtx = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var candidates = await readCtx.WaitlistEntries
            .AsNoTracking()
            .Where(w => w.PreferredDate == notification.Date
                     && w.PreferredTimeSlot == notification.TimeSlotStart
                     && w.Status == WaitlistStatus.Active)
            .OrderBy(w => w.EnrolledAt)
            .Take(MaxFifoIterations)
            .Select(w => new { w.Id, w.PatientId, w.CurrentAppointmentId })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "No active waitlist entries for slot {SpecialtyId}/{Date}/{TimeSlot}",
                notification.SpecialtyId,
                notification.Date,
                notification.TimeSlotStart.ToString("HH\\:mm"));
            return;
        }

        // Step 2 — Iterate in FIFO order; verify eligibility and expire invalid entries.
        foreach (var candidate in candidates)
        {
            await using var writeCtx = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var currentAppt = await writeCtx.Appointments
                .FirstOrDefaultAsync(a => a.Id == candidate.CurrentAppointmentId, cancellationToken);

            if (currentAppt is null || currentAppt.Status != AppointmentStatus.Booked)
            {
                // Edge case: current appointment already cancelled — expire the waitlist entry.
                var entry = await writeCtx.WaitlistEntries.FindAsync([candidate.Id], cancellationToken);
                if (entry is not null)
                {
                    entry.Status = WaitlistStatus.Expired;
                    await writeCtx.SaveChangesAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "WaitlistEntry {WaitlistEntryId} expired: current appointment {CurrentAppointmentId} is no longer Booked",
                    candidate.Id,
                    candidate.CurrentAppointmentId);
                continue;
            }

            // Step 3 — Dispatch slot swap for the first eligible FIFO candidate.
            _logger.LogInformation(
                "Dispatching slot swap for WaitlistEntry {WaitlistEntryId}, Patient {PatientId}",
                candidate.Id,
                candidate.PatientId);

            await _mediator.Send(new ExecuteSlotSwapCommand(
                WaitlistEntryId:        candidate.Id,
                PatientId:              candidate.PatientId,
                CurrentAppointmentId:   candidate.CurrentAppointmentId,
                SpecialtyId:            notification.SpecialtyId,
                PreferredDate:          notification.Date,
                PreferredTimeSlotStart: notification.TimeSlotStart,
                PreferredTimeSlotEnd:   notification.TimeSlotEnd
            ), cancellationToken);

            return; // One candidate dispatched per cancellation event.
        }
    }
}

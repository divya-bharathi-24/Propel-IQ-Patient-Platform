using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Queue.Queries;

namespace Propel.Modules.Queue.Handlers;

/// <summary>
/// Handles <see cref="GetTodayQueueQuery"/> for <c>GET /api/queue/today</c> (US_027, AC-1).
/// <list type="number">
///   <item><b>Step 1 — Load today's appointments</b> via <see cref="IQueueRepository.GetTodayAppointmentsAsync"/>
///         using <c>AsNoTracking()</c> with <c>Patient</c> and <c>QueueEntry</c> navigation properties
///         eagerly loaded and ordered by <c>TimeSlotStart ASC</c> (AD-2).</item>
///   <item><b>Step 2 — Project</b> to <see cref="QueueItemDto"/> — resolves <c>PatientName</c>
///         (Walk-In Guest when <c>PatientId</c> is null) and determines <c>BookingType</c>
///         from <c>CreatedBy</c> presence.</item>
/// </list>
/// </summary>
public sealed class GetTodayQueueQueryHandler
    : IRequestHandler<GetTodayQueueQuery, IReadOnlyList<QueueItemDto>>
{
    private readonly IQueueRepository _queueRepo;
    private readonly ILogger<GetTodayQueueQueryHandler> _logger;

    public GetTodayQueueQueryHandler(
        IQueueRepository queueRepo,
        ILogger<GetTodayQueueQueryHandler> logger)
    {
        _queueRepo = queueRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<QueueItemDto>> Handle(
        GetTodayQueueQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load today's appointments with related navigation properties.
        var appointments = await _queueRepo.GetTodayAppointmentsAsync(cancellationToken);

        _logger.LogDebug(
            "GetTodayQueue: loaded {Count} appointment(s) for {Date}",
            appointments.Count, DateOnly.FromDateTime(DateTime.UtcNow));

        // Step 2 — Project to QueueItemDto.
        // BookingType: PatientId null = anonymous walk-in (US_026, AC-3).
        // ArrivalStatus: Booked is normalised to "Waiting" so the frontend model
        //   ('Waiting' | 'Arrived' | 'Cancelled') stays consistent with the wireframe (SCR-014).
        // QueuePosition: use QueueEntry.Position when present; fall back to the row's
        //   ascending order index so every row has a non-zero position.
        // WaitTime: computed for Waiting (Booked) appointments whose slot has passed.
        // RiskLevel: derived from NoShowRisk.Severity when a score record exists.
        var now = DateTime.UtcNow;
        var items = appointments
            .Select((a, index) =>
            {
                var waitTime = ComputeWaitTime(a, now);
                var arrivalStatus = a.Status == AppointmentStatus.Booked
                    ? "Waiting"
                    : a.Status.ToString();
                return new QueueItemDto(
                    AppointmentId: a.Id,
                    PatientId: a.PatientId,
                    PatientName: a.Patient?.Name ?? "Walk-In Guest",
                    QueuePosition: a.QueueEntry?.Position ?? (index + 1),
                    ChiefComplaint: a.ChiefComplaint,
                    TimeSlotStart: a.TimeSlotStart,
                    WaitTime: waitTime,
                    RiskLevel: a.NoShowRisk?.Severity,
                    BookingType: a.PatientId is null ? "WalkIn" : "SelfBooked",
                    ArrivalStatus: arrivalStatus,
                    ArrivalTimestamp: a.QueueEntry?.ArrivalTime);
            })
            .ToList();

        return items;
    }

    /// <summary>
    /// Returns a human-readable wait string (e.g. "47 min") for Booked appointments
    /// whose scheduled slot has already passed. Returns <c>null</c> in all other cases.
    /// </summary>
    private static string? ComputeWaitTime(Appointment appointment, DateTime now)
    {
        if (appointment.Status != AppointmentStatus.Booked)
            return null;
        if (appointment.TimeSlotStart is null)
            return null;

        var slotUtc = appointment.Date.ToDateTime(appointment.TimeSlotStart.Value, DateTimeKind.Utc);
        var elapsed = (int)(now - slotUtc).TotalMinutes;
        return elapsed > 0 ? $"{elapsed} min" : null;
    }
}

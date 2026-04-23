using MediatR;
using Microsoft.Extensions.Logging;
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
        // BookingType: CreatedBy == Guid.Empty is used as proxy for anonymous walk-in;
        // US_026 sets CreatedBy = staffId for walk-in appointments. PatientId null also
        // indicates an anonymous walk-in (US_026, AC-3).
        var items = appointments
            .Select(a => new QueueItemDto(
                AppointmentId: a.Id,
                PatientName: a.Patient?.Name ?? "Walk-In Guest",
                TimeSlotStart: a.TimeSlotStart,
                BookingType: a.PatientId is null ? "WalkIn" : "SelfBooked",
                ArrivalStatus: a.Status.ToString(),
                ArrivalTimestamp: a.QueueEntry?.ArrivalTime))
            .ToList();

        return items;
    }
}

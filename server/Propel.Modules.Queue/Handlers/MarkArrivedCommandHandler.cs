using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Queue.Commands;
using Propel.Modules.Queue.Exceptions;

namespace Propel.Modules.Queue.Handlers;

/// <summary>
/// Handles <see cref="MarkArrivedCommand"/> for <c>PATCH /api/queue/{appointmentId}/arrived</c>
/// (US_027, AC-2, AC-4).
/// <list type="number">
///   <item><b>Step 1 — Resolve staffId from JWT claims</b> (OWASP A01 — never from request body).</item>
///   <item><b>Step 2 — Load appointment with QueueEntry</b> via <see cref="IQueueRepository.GetAppointmentWithQueueEntryAsync"/>.</item>
///   <item><b>Step 3 — Today-only guard</b>: throws <see cref="QueueBusinessRuleViolationException"/> (→ HTTP 400) if not today.</item>
///   <item><b>Step 4 — Mutate</b>: <c>Appointment.Status = Arrived</c>; <c>QueueEntry.Status = Called</c>; <c>QueueEntry.ArrivalTime = UtcNow</c>.</item>
///   <item><b>Step 5 — Atomic commit</b>: single <c>SaveChangesAsync()</c> for Appointment + QueueEntry (AD-2).</item>
///   <item><b>Step 6 — Audit log INSERT</b>: immutable <see cref="AuditLog"/> entry via <see cref="IAuditLogRepository"/> (AD-7).</item>
/// </list>
/// </summary>
public sealed class MarkArrivedCommandHandler : IRequestHandler<MarkArrivedCommand, Unit>
{
    private readonly IQueueRepository _queueRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MarkArrivedCommandHandler> _logger;

    public MarkArrivedCommandHandler(
        IQueueRepository queueRepo,
        IAuditLogRepository auditLogRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MarkArrivedCommandHandler> logger)
    {
        _queueRepo = queueRepo;
        _auditLogRepo = auditLogRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Unit> Handle(MarkArrivedCommand request, CancellationToken cancellationToken)
    {
        // Step 1 — Resolve staffId from JWT NameIdentifier claim (OWASP A01).
        var staffIdClaim = _httpContextAccessor.HttpContext!.User
            .FindFirstValue(ClaimTypes.NameIdentifier)!;
        var staffId = Guid.Parse(staffIdClaim);

        var ipAddress = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();

        // Step 2 — Load appointment with QueueEntry.
        var appointment = await _queueRepo.GetAppointmentWithQueueEntryAsync(
            request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Appointment '{request.AppointmentId}' was not found.");

        // Step 3 — Today-only guard: prevents off-day manipulation (FR-027, FluentValidation business rule).
        if (appointment.Date != DateOnly.FromDateTime(DateTime.UtcNow))
        {
            _logger.LogWarning(
                "MarkArrived_OffDay: AppointmentId={AppointmentId} AppointmentDate={Date} StaffId={StaffId}",
                request.AppointmentId, appointment.Date, staffId);
            throw new QueueBusinessRuleViolationException(
                "Arrived marking is restricted to today's appointments only.");
        }

        // Step 4 — Mutate appointment and queue entry.
        appointment.Status = AppointmentStatus.Arrived;

        if (appointment.QueueEntry is not null)
        {
            appointment.QueueEntry.Status = QueueEntryStatus.Called;
            appointment.QueueEntry.ArrivalTime = DateTime.UtcNow;
        }

        // Step 5 — Atomic commit: Appointment + QueueEntry in one SaveChangesAsync.
        await _queueRepo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "MarkArrived_Success: AppointmentId={AppointmentId} StaffId={StaffId}",
            request.AppointmentId, staffId);

        // Step 6 — Audit log INSERT (AD-7): immutable record — must not be rolled back.
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = staffId,
            Action = "ArrivalMarked",
            EntityType = "Appointment",
            EntityId = appointment.Id,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Unit.Value;
    }
}

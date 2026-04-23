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
/// Handles <see cref="RevertArrivedCommand"/> for <c>PATCH /api/queue/{appointmentId}/revert-arrived</c>
/// (US_027, edge case — accidental mark-arrived reversal).
/// <list type="number">
///   <item><b>Step 1 — Resolve staffId from JWT claims</b> (OWASP A01 — never from request body).</item>
///   <item><b>Step 2 — Load appointment with QueueEntry</b>.</item>
///   <item><b>Step 3 — Same-day restriction</b>: <c>QueueEntry.ArrivalTime</c> must be from today UTC;
///         throws <see cref="QueueBusinessRuleViolationException"/> (→ HTTP 400) otherwise.</item>
///   <item><b>Step 4 — Mutate</b>: <c>Appointment.Status = Booked</c>; <c>QueueEntry.Status = Waiting</c>;
///         <c>QueueEntry.ArrivalTime = null</c>.</item>
///   <item><b>Step 5 — Atomic commit</b>: single <c>SaveChangesAsync()</c> for Appointment + QueueEntry (AD-2).</item>
///   <item><b>Step 6 — Audit log INSERT</b>: immutable <see cref="AuditLog"/> entry (AD-7).</item>
/// </list>
/// </summary>
public sealed class RevertArrivedCommandHandler : IRequestHandler<RevertArrivedCommand, Unit>
{
    private readonly IQueueRepository _queueRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RevertArrivedCommandHandler> _logger;

    public RevertArrivedCommandHandler(
        IQueueRepository queueRepo,
        IAuditLogRepository auditLogRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RevertArrivedCommandHandler> logger)
    {
        _queueRepo = queueRepo;
        _auditLogRepo = auditLogRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Unit> Handle(RevertArrivedCommand request, CancellationToken cancellationToken)
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

        // Step 3 — Same-day restriction: ArrivalTime must be from today UTC (FluentValidation business rule).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (appointment.QueueEntry?.ArrivalTime is null
            || DateOnly.FromDateTime(appointment.QueueEntry.ArrivalTime.Value.ToUniversalTime()) != today)
        {
            _logger.LogWarning(
                "RevertArrived_Rejected: AppointmentId={AppointmentId} ArrivalTime={ArrivalTime} StaffId={StaffId}",
                request.AppointmentId, appointment.QueueEntry?.ArrivalTime, staffId);
            throw new QueueBusinessRuleViolationException(
                "Arrival reversal is only allowed on the same calendar day.");
        }

        // Step 4 — Mutate: reset to pre-arrived state.
        appointment.Status = AppointmentStatus.Booked;
        appointment.QueueEntry.Status = QueueEntryStatus.Waiting;
        appointment.QueueEntry.ArrivalTime = null;

        // Step 5 — Atomic commit: Appointment + QueueEntry in one SaveChangesAsync.
        await _queueRepo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RevertArrived_Success: AppointmentId={AppointmentId} StaffId={StaffId}",
            request.AppointmentId, staffId);

        // Step 6 — Audit log INSERT (AD-7): immutable record — must not be rolled back.
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = staffId,
            Action = "ArrivalReverted",
            EntityType = "Appointment",
            EntityId = appointment.Id,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Unit.Value;
    }
}

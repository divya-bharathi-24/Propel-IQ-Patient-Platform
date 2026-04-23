using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Exceptions;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="CancelWaitlistPreferenceCommand"/> for
/// <c>PATCH /api/waitlist/{id}/cancel</c> (US_023, AC-4).
/// <list type="number">
///   <item><b>Step 1 — Load entry</b>: fetch <see cref="WaitlistEntry"/> by id;
///         throw <see cref="KeyNotFoundException"/> → HTTP 404 if not found.</item>
///   <item><b>Step 2 — Ownership check</b>: <c>entry.PatientId != command.PatientId</c> →
///         throw <see cref="ForbiddenAccessException"/> → HTTP 403 (OWASP A01).</item>
///   <item><b>Step 3 — Status guard</b>: entry must be <c>Active</c>;
///         otherwise throw <see cref="BusinessRuleViolationException"/> → HTTP 400.</item>
///   <item><b>Step 4 — Set Expired</b>: mutate and persist via <c>SaveAsync()</c>.</item>
///   <item><b>Step 5 — Audit log</b>: immutable <see cref="AuditLog"/> INSERT (AD-7).</item>
/// </list>
/// </summary>
public sealed class CancelWaitlistPreferenceCommandHandler
    : IRequestHandler<CancelWaitlistPreferenceCommand, Unit>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CancelWaitlistPreferenceCommandHandler> _logger;

    public CancelWaitlistPreferenceCommandHandler(
        IWaitlistRepository waitlistRepo,
        IAuditLogRepository auditLogRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CancelWaitlistPreferenceCommandHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _auditLogRepo = auditLogRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Unit> Handle(
        CancelWaitlistPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();

        // Step 1 — Load WaitlistEntry; 404 if not found.
        var entry = await _waitlistRepo.GetByIdAsync(command.WaitlistEntryId, cancellationToken);

        if (entry is null)
        {
            _logger.LogWarning(
                "CancelWaitlistPreference_NotFound: WaitlistEntryId={WaitlistEntryId} PatientId={PatientId}",
                command.WaitlistEntryId, command.PatientId);
            throw new KeyNotFoundException(
                $"Waitlist entry '{command.WaitlistEntryId}' was not found.");
        }

        // Step 2 — Ownership check (OWASP A01 — Broken Access Control).
        if (entry.PatientId != command.PatientId)
        {
            _logger.LogWarning(
                "CancelWaitlistPreference_Forbidden: WaitlistEntryId={WaitlistEntryId} RequestingPatientId={RequestingPatientId} OwnerPatientId={OwnerPatientId}",
                command.WaitlistEntryId, command.PatientId, entry.PatientId);
            throw new ForbiddenAccessException(
                "You are not authorised to cancel this waitlist entry.");
        }

        // Step 3 — Status guard: only Active entries can be cancelled.
        if (entry.Status != WaitlistStatus.Active)
        {
            _logger.LogWarning(
                "CancelWaitlistPreference_NotActive: WaitlistEntryId={WaitlistEntryId} CurrentStatus={Status}",
                command.WaitlistEntryId, entry.Status);
            throw new BusinessRuleViolationException("Waitlist entry is not active.");
        }

        // Step 4 — Set status to Expired and persist.
        entry.Status = WaitlistStatus.Expired;
        await _waitlistRepo.SaveAsync(cancellationToken);

        _logger.LogInformation(
            "WaitlistPreferenceCancelled: WaitlistEntryId={WaitlistEntryId} PatientId={PatientId}",
            command.WaitlistEntryId, command.PatientId);

        // Step 5 — Audit log INSERT (AD-7).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.PatientId,
            PatientId = command.PatientId,
            Role = "Patient",
            Action = "WaitlistPreferenceCancelled",
            EntityType = nameof(WaitlistEntry),
            EntityId = command.WaitlistEntryId,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Unit.Value;
    }
}

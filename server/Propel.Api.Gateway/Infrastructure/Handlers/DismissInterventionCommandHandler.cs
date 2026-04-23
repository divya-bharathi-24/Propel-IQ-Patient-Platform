using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Risk.Commands;
using Propel.Modules.Risk.Exceptions;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// Handles <see cref="DismissInterventionCommand"/> for
/// <c>PATCH /api/risk/interventions/{interventionId}/dismiss</c> (US_032, AC-3, FR-030).
/// <list type="number">
///   <item><b>Step 1</b> — Resolve <c>staffId</c> from JWT <c>NameIdentifier</c> claim (OWASP A01).</item>
///   <item><b>Step 2</b> — Load the <see cref="RiskIntervention"/> row; 404 if not found.</item>
///   <item><b>Step 3</b> — Guard: throw <see cref="RiskBusinessRuleViolationException"/> (→ 400)
///         if the intervention is not in <c>Pending</c> status.</item>
///   <item><b>Step 4</b> — Mutate: <c>status = Dismissed</c>, <c>staffId</c>, <c>acknowledgedAt</c>,
///         <c>dismissalReason</c> (optional, max 500 chars enforced by validator).</item>
///   <item><b>Step 5</b> — Atomic <c>SaveChangesAsync</c>.</item>
///   <item><b>Step 6</b> — Append immutable <see cref="AuditLog"/> entry <c>InterventionDismissed</c> (AD-7, FR-030).</item>
/// </list>
/// </summary>
public sealed class DismissInterventionCommandHandler : IRequestHandler<DismissInterventionCommand, Unit>
{
    private readonly AppDbContext _db;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DismissInterventionCommandHandler> _logger;

    public DismissInterventionCommandHandler(
        AppDbContext db,
        IAuditLogRepository auditLogRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DismissInterventionCommandHandler> logger)
    {
        _db                  = db;
        _auditLogRepo        = auditLogRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger              = logger;
    }

    public async Task<Unit> Handle(DismissInterventionCommand request, CancellationToken cancellationToken)
    {
        // Step 1 — Resolve staffId from JWT NameIdentifier claim (OWASP A01 — never from request body).
        var staffId = Guid.Parse(
            _httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var ipAddress = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();

        // Step 2 — Load the intervention.
        var intervention = await _db.RiskInterventions
            .FirstOrDefaultAsync(i => i.Id == request.InterventionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"RiskIntervention '{request.InterventionId}' was not found.");

        // Step 3 — Guard: only Pending interventions can be dismissed.
        if (intervention.Status != InterventionStatus.Pending)
        {
            _logger.LogWarning(
                "DismissIntervention_AlreadyAcknowledged: InterventionId={InterventionId} Status={Status} StaffId={StaffId}",
                request.InterventionId, intervention.Status, staffId);
            throw new RiskBusinessRuleViolationException(
                "Intervention has already been acknowledged and cannot be dismissed again.");
        }

        // Step 4 — Mutate.
        intervention.Status          = InterventionStatus.Dismissed;
        intervention.StaffId         = staffId;
        intervention.AcknowledgedAt  = DateTime.UtcNow;
        intervention.DismissalReason = request.DismissalReason;

        // Step 5 — Atomic commit.
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DismissIntervention_Success: InterventionId={InterventionId} AppointmentId={AppointmentId} StaffId={StaffId}",
            intervention.Id, intervention.AppointmentId, staffId);

        // Step 6 — Append immutable audit log (AD-7, FR-030).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            UserId     = staffId,
            Action     = "InterventionDismissed",
            EntityType = nameof(RiskIntervention),
            EntityId   = intervention.Id,
            IpAddress  = ipAddress,
            Timestamp  = DateTime.UtcNow
        }, cancellationToken);

        return Unit.Value;
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Risk.Events;
using Propel.Modules.Risk.Interfaces;

namespace Propel.Modules.Risk.Commands;

/// <summary>
/// Handles <see cref="CalculateNoShowRiskCommand"/> (us_031, task_002, AC-1, AC-4).
/// <list type="number">
///   <item><b>Step 1</b> — Call <see cref="INoShowRiskCalculator.CalculateAsync"/> to get the weighted score + factors.</item>
///   <item><b>Step 2</b> — UPSERT <see cref="NoShowRisk"/>: update if exists, insert if new (AC-4).</item>
///   <item><b>Step 3</b> — Append immutable <see cref="AuditLog"/> entry (AD-7).</item>
///   <item><b>Step 4</b> — Publish <see cref="NoShowRiskAssessedEvent"/> to trigger intervention generation (US_032, task_002).</item>
/// </list>
/// </summary>
public sealed class CalculateNoShowRiskCommandHandler : IRequestHandler<CalculateNoShowRiskCommand, Unit>
{
    private readonly INoShowRiskCalculator _calculator;
    private readonly INoShowRiskRepository _riskRepo;
    private readonly IAuditLogRepository   _auditLogRepo;
    private readonly IPublisher            _publisher;
    private readonly ILogger<CalculateNoShowRiskCommandHandler> _logger;

    public CalculateNoShowRiskCommandHandler(
        INoShowRiskCalculator calculator,
        INoShowRiskRepository riskRepo,
        IAuditLogRepository auditLogRepo,
        IPublisher publisher,
        ILogger<CalculateNoShowRiskCommandHandler> logger)
    {
        _calculator   = calculator;
        _riskRepo     = riskRepo;
        _auditLogRepo = auditLogRepo;
        _publisher    = publisher;
        _logger       = logger;
    }

    public async Task<Unit> Handle(
        CalculateNoShowRiskCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Calculate score.
        var result = await _calculator.CalculateAsync(request.AppointmentId, cancellationToken);
        if (result is null)
        {
            _logger.LogWarning(
                "NoShowRisk_SkippedNotFound: appointment {AppointmentId} not found — skipping risk calculation.",
                request.AppointmentId);
            return Unit.Value;
        }

        // Step 2 — UPSERT NoShowRisk (AC-4: update existing or insert new).
        var factorsJson = JsonSerializer.SerializeToDocument(result.Factors);
        var existing    = await _riskRepo.GetByAppointmentIdAsync(request.AppointmentId, cancellationToken);

        if (existing is not null)
        {
            existing.Score        = (decimal)result.Score;
            existing.Severity     = result.Severity;
            existing.Factors      = factorsJson;
            existing.CalculatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new NoShowRisk
            {
                Id            = Guid.NewGuid(),
                AppointmentId = request.AppointmentId,
                Score         = (decimal)result.Score,
                Severity      = result.Severity,
                Factors       = factorsJson,
                CalculatedAt  = DateTime.UtcNow
            };
        }

        await _riskRepo.UpsertAsync(existing, cancellationToken);

        _logger.LogInformation(
            "NoShowRisk_Calculated: AppointmentId={AppointmentId} Score={Score} Severity={Severity} DegradedMode={DegradedMode}",
            request.AppointmentId, result.Score, result.Severity, result.DegradedMode);

        // Step 3 — Append audit log (AD-7).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            Action     = "NoShowRiskCalculated",
            EntityType = nameof(NoShowRisk),
            EntityId   = existing.Id,
            Details    = JsonSerializer.SerializeToDocument(new
            {
                score        = result.Score,
                severity     = result.Severity,
                degradedMode = result.DegradedMode
            }),
            Timestamp  = DateTime.UtcNow
        }, cancellationToken);

        // Step 4 — Publish NoShowRiskAssessedEvent to trigger intervention generation (US_032, task_002).
        // Fire-and-forget safety: NoShowRiskAssessedEventHandler wraps its body in try/catch (AG-6).
        await _publisher.Publish(
            new NoShowRiskAssessedEvent(
                AppointmentId: existing.AppointmentId,
                NoShowRiskId:  existing.Id,
                Score:         existing.Score),
            cancellationToken);

        return Unit.Value;
    }
}

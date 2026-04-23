using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Modules.Risk.Events;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// Handles <see cref="NoShowRiskAssessedEvent"/> published by
/// <c>CalculateNoShowRiskCommandHandler</c> after each <c>NoShowRisk</c> UPSERT (US_032, task_002).
/// <list type="number">
///   <item><b>High severity (score &gt; 0.66)</b> — Idempotent INSERT of two
///         <c>RiskIntervention</c> rows (<c>AdditionalReminder</c> and <c>CallbackRequest</c>,
///         both <c>Pending</c>). Skips insertion if any Pending rows already exist for
///         the appointment to prevent duplicates on re-scoring (AC-2, AC-4).</item>
///   <item><b>Non-High (score ≤ 0.66)</b> — Auto-clears existing Pending interventions by
///         setting <c>status = AutoCleared</c> (edge case: score dropped before acknowledgement).</item>
/// </list>
/// <para>
/// <b>AG-6 compliance</b>: the entire handler body is wrapped in try/catch. Any exception is
/// logged at Warning level and swallowed — this handler MUST NOT throw and must not disrupt
/// the primary risk-scoring transaction.
/// </para>
/// </summary>
public sealed class NoShowRiskAssessedEventHandler : INotificationHandler<NoShowRiskAssessedEvent>
{
    private readonly AppDbContext _db;
    private readonly ILogger<NoShowRiskAssessedEventHandler> _logger;

    public NoShowRiskAssessedEventHandler(AppDbContext db, ILogger<NoShowRiskAssessedEventHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task Handle(NoShowRiskAssessedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            if (notification.Score > 0.66m)
            {
                // High severity — idempotent: only insert if no Pending rows already exist.
                var hasPending = await _db.RiskInterventions.AnyAsync(
                    i => i.AppointmentId == notification.AppointmentId
                      && i.Status == InterventionStatus.Pending,
                    cancellationToken);

                if (!hasPending)
                {
                    _db.RiskInterventions.AddRange(
                        new RiskIntervention
                        {
                            Id            = Guid.NewGuid(),
                            AppointmentId = notification.AppointmentId,
                            NoShowRiskId  = notification.NoShowRiskId,
                            Type          = InterventionType.AdditionalReminder,
                            Status        = InterventionStatus.Pending
                        },
                        new RiskIntervention
                        {
                            Id            = Guid.NewGuid(),
                            AppointmentId = notification.AppointmentId,
                            NoShowRiskId  = notification.NoShowRiskId,
                            Type          = InterventionType.CallbackRequest,
                            Status        = InterventionStatus.Pending
                        });

                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "NoShowRiskAssessed_InterventionsCreated: AppointmentId={AppointmentId} NoShowRiskId={NoShowRiskId} Score={Score}",
                        notification.AppointmentId, notification.NoShowRiskId, notification.Score);
                }
                else
                {
                    _logger.LogDebug(
                        "NoShowRiskAssessed_InterventionsAlreadyExist: AppointmentId={AppointmentId} — idempotent skip.",
                        notification.AppointmentId);
                }
            }
            else
            {
                // Score dropped to Medium/Low — auto-clear any Pending interventions (edge case).
                var pendingInterventions = await _db.RiskInterventions
                    .Where(i => i.AppointmentId == notification.AppointmentId
                             && i.Status == InterventionStatus.Pending)
                    .ToListAsync(cancellationToken);

                if (pendingInterventions.Count > 0)
                {
                    pendingInterventions.ForEach(i => i.Status = InterventionStatus.AutoCleared);
                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "NoShowRiskAssessed_InterventionsAutoCleared: AppointmentId={AppointmentId} Count={Count} Score={Score}",
                        notification.AppointmentId, pendingInterventions.Count, notification.Score);
                }
            }
        }
        catch (Exception ex)
        {
            // AG-6: handler must never throw — log warning and return to protect the caller.
            _logger.LogWarning(
                ex,
                "NoShowRiskAssessed_HandlerFailed: AppointmentId={AppointmentId} NoShowRiskId={NoShowRiskId} — intervention rows may not have been updated.",
                notification.AppointmentId, notification.NoShowRiskId);
        }
    }
}

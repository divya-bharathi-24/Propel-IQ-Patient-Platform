using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Risk.Exceptions;
using Propel.Modules.Risk.Interfaces;
using Propel.Modules.Risk.Models;

namespace Propel.Modules.Risk.Services;

/// <summary>
/// Rule-based no-show risk calculator implementing <see cref="INoShowRiskCalculator"/> (us_031, task_002, AC-1, AC-3).
/// Computes a weighted score from five behavioral factors and applies an optional AI augmentation
/// delta via <see cref="IAiNoShowRiskAugmenter"/>. When the AI augmenter is unavailable, the
/// base rule score is used unchanged and <c>DegradedMode = true</c> is set (AC-3).
///
/// <para><b>Factor weights and scoring rules:</b></para>
/// <list type="table">
///   <listheader><term>Factor</term><term>Weight</term><term>Rules</term></listheader>
///   <item><term>PriorNoShowHistory</term><term>0.35</term><term>0 = 0.0; 1 = 0.5; 2+ = 1.0; no history = neutral 0.5</term></item>
///   <item><term>BookingLeadTime</term><term>0.25</term><term>&gt;14d = 0.2; 7–14d = 0.5; 3–6d = 0.7; &lt;3d = 1.0</term></item>
///   <item><term>AppointmentType</term><term>0.15</term><term>Routine = 0.5; Specialist = 0.3; Emergency = 0.1; unknown = 0.5</term></item>
///   <item><term>IntakeCompletion</term><term>0.15</term><term>Completed = 0.0; Not completed = 0.8; No record = 0.5</term></item>
///   <item><term>ReminderEngagement</term><term>0.10</term><term>Delivered = 0.2; Not sent = 0.5; Sent but undelivered = 0.8</term></item>
/// </list>
/// <para>Missing data for any factor → neutral score 0.5 + DataAvailability factor entry added.</para>
/// </summary>
public sealed class RuleBasedNoShowRiskCalculator : INoShowRiskCalculator
{
    private const double PriorNoShowWeight   = 0.35;
    private const double LeadTimeWeight      = 0.25;
    private const double AppointmentTypeWeight = 0.15;
    private const double IntakeWeight        = 0.15;
    private const double ReminderWeight      = 0.10;

    private readonly INoShowRiskRepository _repository;
    private readonly IAiNoShowRiskAugmenter _aiAugmenter;
    private readonly ILogger<RuleBasedNoShowRiskCalculator> _logger;

    public RuleBasedNoShowRiskCalculator(
        INoShowRiskRepository repository,
        IAiNoShowRiskAugmenter aiAugmenter,
        ILogger<RuleBasedNoShowRiskCalculator> logger)
    {
        _repository  = repository;
        _aiAugmenter = aiAugmenter;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public async Task<RiskScoreResult?> CalculateAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var data = await _repository.GetRiskInputDataAsync(appointmentId, cancellationToken);
        if (data is null)
            return null;

        var factors       = new List<RiskFactor>();
        var dataInsufficient = false;

        // ── Factor 1: Prior no-show history (weight 0.35) ────────────────────
        double priorScore;
        string? priorNote;
        if (data.PriorNoShowCount is null)
        {
            priorScore = 0.5;
            priorNote  = "neutral (no history)";
            dataInsufficient = true;
        }
        else
        {
            priorScore = data.PriorNoShowCount.Value switch
            {
                0    => 0.0,
                1    => 0.5,
                >= 2 => 1.0,
                _    => 0.5
            };
            priorNote = null;
        }
        factors.Add(new RiskFactor(
            Name:         "PriorNoShowHistory",
            Score:        priorScore,
            Weight:       PriorNoShowWeight,
            Contribution: priorScore * PriorNoShowWeight,
            Note:         priorNote));

        // ── Factor 2: Booking lead time (weight 0.25) ────────────────────────
        var leadDays  = (data.AppointmentDate.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow.Date).Days;
        double leadScore = leadDays switch
        {
            > 14 => 0.2,
            >= 7 => 0.5,
            >= 3 => 0.7,
            _    => 1.0
        };
        factors.Add(new RiskFactor(
            Name:         "BookingLeadTime",
            Score:        leadScore,
            Weight:       LeadTimeWeight,
            Contribution: leadScore * LeadTimeWeight,
            Note:         $"{leadDays} day(s) until appointment"));

        // ── Factor 3: Appointment type / specialty (weight 0.15) ─────────────
        double typeScore;
        string? typeNote = null;
        var specialtyName = data.SpecialtyName?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(specialtyName))
        {
            typeScore = 0.5;
            typeNote  = "unknown specialty";
            dataInsufficient = true;
        }
        else if (specialtyName.Contains("emergency"))
        {
            typeScore = 0.1;
        }
        else if (specialtyName.Contains("specialist") || specialtyName.Contains("specialist"))
        {
            typeScore = 0.3;
        }
        else
        {
            // Default to Routine scoring (covers "General", "Routine", and any unmapped specialties)
            typeScore = 0.5;
        }
        factors.Add(new RiskFactor(
            Name:         "AppointmentType",
            Score:        typeScore,
            Weight:       AppointmentTypeWeight,
            Contribution: typeScore * AppointmentTypeWeight,
            Note:         typeNote));

        // ── Factor 4: Intake completion (weight 0.15) ────────────────────────
        double intakeScore;
        string? intakeNote = null;
        if (data.IntakeCompleted is null)
        {
            intakeScore = 0.5;
            intakeNote  = "no intake record";
            dataInsufficient = true;
        }
        else
        {
            intakeScore = data.IntakeCompleted.Value ? 0.0 : 0.8;
        }
        factors.Add(new RiskFactor(
            Name:         "IntakeCompletion",
            Score:        intakeScore,
            Weight:       IntakeWeight,
            Contribution: intakeScore * IntakeWeight,
            Note:         intakeNote));

        // ── Factor 5: Reminder engagement (weight 0.10) ──────────────────────
        double reminderScore;
        string reminderNote;
        if (data.AnyNotificationDelivered)
        {
            reminderScore = 0.2;
            reminderNote  = "reminder delivered";
        }
        else if (!data.AnyNotificationSent)
        {
            reminderScore = 0.5;
            reminderNote  = "no reminders sent";
        }
        else
        {
            reminderScore = 0.8;
            reminderNote  = "reminder sent but not delivered";
        }
        factors.Add(new RiskFactor(
            Name:         "ReminderEngagement",
            Score:        reminderScore,
            Weight:       ReminderWeight,
            Contribution: reminderScore * ReminderWeight,
            Note:         reminderNote));

        // ── DataAvailability factor (edge case: missing data) ─────────────────
        if (dataInsufficient)
        {
            factors.Add(new RiskFactor(
                Name:         "DataAvailability",
                Score:        0.0,
                Weight:       0.0,
                Contribution: 0.0,
                Note:         "Insufficient data for full assessment"));
        }

        // ── Weighted sum → base score ─────────────────────────────────────────
        double baseScore = factors
            .Where(f => f.Name != "DataAvailability")
            .Sum(f => f.Contribution);

        baseScore = Math.Clamp(baseScore, 0.0, 1.0);

        // ── AI augmentation delta (AC-3: degrade gracefully on unavailability) ─
        bool degradedMode = false;
        double finalScore = baseScore;

        if (data.PatientId is not null)
        {
            try
            {
                var delta = await _aiAugmenter.GetAugmentationDeltaAsync(
                    data.PatientId.Value, appointmentId, baseScore, cancellationToken);
                finalScore = Math.Clamp(baseScore + delta, 0.0, 1.0);
            }
            catch (AiNoShowRiskUnavailableException)
            {
                degradedMode = true;
                _logger.LogWarning(
                    "NoShowRisk_AiDegraded {@AppointmentId}", appointmentId);
            }
        }

        var severity = finalScore switch
        {
            < 0.35 => "Low",
            <= 0.70 => "Medium",
            _ => "High"
        };

        return new RiskScoreResult(
            Score:       finalScore,
            Severity:    severity,
            Factors:     factors,
            DegradedMode: degradedMode);
    }
}

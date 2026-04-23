using FluentValidation;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Notification.Validators;

/// <summary>
/// FluentValidation rules for <see cref="UpdateReminderIntervalsCommand"/> (US_033, AC-3).
/// Enforces:
/// <list type="bullet">
///   <item><c>IntervalHours</c> must not be null or empty.</item>
///   <item>Maximum 10 interval values (operational ceiling).</item>
///   <item>Each value must be greater than zero (positive hours).</item>
///   <item>Each value must not exceed 168 hours (7-day maximum lead time).</item>
///   <item>No duplicate values within the submitted array.</item>
/// </list>
/// Registered automatically by <c>AddValidatorsFromAssemblies</c> in <c>Program.cs</c>.
/// The MediatR <c>ValidationBehavior&lt;,&gt;</c> pipeline behaviour executes this validator
/// before the command handler is invoked, returning HTTP 400 with structured errors on failure.
/// </summary>
public sealed class UpdateReminderIntervalsValidator : AbstractValidator<UpdateReminderIntervalsCommand>
{
    private const int MaxIntervals  = 10;
    private const int MaxHours      = 168; // 7 days

    public UpdateReminderIntervalsValidator()
    {
        RuleFor(c => c.IntervalHours)
            .NotNull()
            .WithMessage("IntervalHours must not be null.")
            .NotEmpty()
            .WithMessage("IntervalHours must contain at least one interval value.");

        RuleFor(c => c.IntervalHours)
            .Must(arr => arr is null || arr.Length <= MaxIntervals)
            .WithMessage($"IntervalHours must not contain more than {MaxIntervals} values.");

        RuleForEach(c => c.IntervalHours)
            .GreaterThan(0)
            .WithMessage("Each interval value must be greater than zero.")
            .LessThanOrEqualTo(MaxHours)
            .WithMessage($"Each interval value must not exceed {MaxHours} hours (7 days).");

        RuleFor(c => c.IntervalHours)
            .Must(arr => arr is null || arr.Length == arr.Distinct().Count())
            .WithMessage("IntervalHours must not contain duplicate values.");
    }
}

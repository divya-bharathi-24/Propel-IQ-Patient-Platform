using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="SyncLocalDraftCommand"/> (US_030, AC-3).
/// <para>
/// Enforces structural integrity:
/// <list type="bullet">
///   <item><c>AppointmentId</c> must not be empty.</item>
///   <item><c>LocalTimestamp</c> must not be in the future beyond a ±2-minute clock-skew
///         tolerance (prevents abuse via future-dated timestamps to force apply).</item>
///   <item><c>PatientId</c> must be resolvable from the JWT token.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SyncLocalDraftCommandValidator : AbstractValidator<SyncLocalDraftCommand>
{
    /// <summary>
    /// Clock-skew tolerance for <c>LocalTimestamp</c> validation (task spec: ±2 minutes).
    /// Prevents the validator from rejecting timestamps from clients with minor clock drift.
    /// </summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(2);

    public SyncLocalDraftCommandValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("appointmentId must not be empty.");

        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("patientId must be resolvable from the JWT token.");

        RuleFor(x => x.LocalFields)
            .NotNull().WithMessage("localFields must not be null.");

        RuleFor(x => x.LocalTimestamp)
            .Must(ts => ts <= DateTimeOffset.UtcNow.Add(ClockSkewTolerance))
            .WithMessage($"localTimestamp must not be more than {ClockSkewTolerance.TotalMinutes} minutes in the future.");
    }
}

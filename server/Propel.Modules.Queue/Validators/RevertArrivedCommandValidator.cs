using FluentValidation;
using Propel.Modules.Queue.Commands;

namespace Propel.Modules.Queue.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RevertArrivedCommand"/> (US_027, edge case).
/// <list type="bullet">
///   <item><c>AppointmentId</c> — must be a non-empty GUID.</item>
/// </list>
/// The same-day-only restriction is enforced inside <c>RevertArrivedCommandHandler</c>
/// via the <c>ArrivalTime</c> UTC date check — it requires a DB round-trip and must raise
/// a typed domain exception.
/// </summary>
public sealed class RevertArrivedCommandValidator : AbstractValidator<RevertArrivedCommand>
{
    public RevertArrivedCommandValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty()
            .WithMessage("'AppointmentId' must not be empty.");
    }
}

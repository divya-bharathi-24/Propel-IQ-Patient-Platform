using FluentValidation;
using Propel.Modules.Queue.Commands;

namespace Propel.Modules.Queue.Validators;

/// <summary>
/// FluentValidation validator for <see cref="MarkArrivedCommand"/> (US_027, AC-2).
/// <list type="bullet">
///   <item><c>AppointmentId</c> — must be a non-empty GUID.</item>
/// </list>
/// The today-only business rule is enforced inside <c>MarkArrivedCommandHandler</c>
/// rather than here, because it requires a database round-trip and must raise a typed
/// domain exception mapping to HTTP 400 (OWASP A01).
/// </summary>
public sealed class MarkArrivedCommandValidator : AbstractValidator<MarkArrivedCommand>
{
    public MarkArrivedCommandValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty()
            .WithMessage("'AppointmentId' must not be empty.");
    }
}

using FluentValidation;
using Propel.Modules.Appointment.Commands;

namespace Propel.Modules.Appointment.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CancelAppointmentCommand"/> (US_020, task_002).
/// <list type="bullet">
///   <item><c>AppointmentId</c> — must be a non-empty GUID.</item>
///   <item><c>CancellationReason</c> — optional; when provided must not exceed 500 characters.</item>
/// </list>
/// Domain pre-checks (ownership, future-date rule) are enforced inside
/// <c>CancelAppointmentCommandHandler</c> rather than here, because they require a database
/// round-trip and must raise typed domain exceptions (HTTP 403 / HTTP 400 respectively).
/// <c>PatientId</c> is intentionally absent from this validator; it is resolved from JWT claims
/// inside the handler and never accepted from the request body (OWASP A01).
/// </summary>
public sealed class CancelAppointmentValidator : AbstractValidator<CancelAppointmentCommand>
{
    public CancelAppointmentValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty()
            .WithMessage("'AppointmentId' must not be empty.");

        RuleFor(x => x.CancellationReason)
            .MaximumLength(500)
            .WithMessage("'CancellationReason' must not exceed 500 characters.")
            .When(x => x.CancellationReason is not null);
    }
}

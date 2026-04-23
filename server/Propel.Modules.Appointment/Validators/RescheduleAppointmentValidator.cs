using FluentValidation;
using Propel.Modules.Appointment.Commands;

namespace Propel.Modules.Appointment.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RescheduleAppointmentCommand"/> (US_020, AC-3, task_003).
/// <list type="bullet">
///   <item><c>OriginalAppointmentId</c> — must be a non-empty GUID.</item>
///   <item><c>SpecialtyId</c> — must be a non-empty GUID.</item>
///   <item><c>NewDate</c> — must be today or a future date (cannot reschedule to the past).</item>
///   <item><c>NewTimeSlotStart</c> — must be a non-default value.</item>
///   <item><c>NewTimeSlotEnd</c> — must be a non-default value and must be after <c>NewTimeSlotStart</c>.</item>
/// </list>
/// <para>
/// Domain pre-checks (ownership, past-appointment rule, slot availability) are enforced inside
/// <c>RescheduleAppointmentCommandHandler</c>, as they require database access and must raise
/// typed domain exceptions (HTTP 403 / HTTP 400 / HTTP 409 respectively).
/// </para>
/// <para>
/// <c>PatientId</c> is intentionally absent from this validator; it is resolved from the JWT
/// <c>sub</c> claim inside the controller and is never accepted from the request body (OWASP A01).
/// </para>
/// </summary>
public sealed class RescheduleAppointmentValidator : AbstractValidator<RescheduleAppointmentCommand>
{
    public RescheduleAppointmentValidator()
    {
        RuleFor(x => x.OriginalAppointmentId)
            .NotEmpty()
            .WithMessage("'OriginalAppointmentId' must not be empty.");

        RuleFor(x => x.SpecialtyId)
            .NotEmpty()
            .WithMessage("'SpecialtyId' must not be empty.");

        RuleFor(x => x.NewDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("'NewDate' must be today or a future date. Cannot reschedule to a past date.");

        RuleFor(x => x.NewTimeSlotStart)
            .NotEqual(default(TimeOnly))
            .WithMessage("'NewTimeSlotStart' must not be empty.");

        RuleFor(x => x.NewTimeSlotEnd)
            .NotEqual(default(TimeOnly))
            .WithMessage("'NewTimeSlotEnd' must not be empty.")
            .GreaterThan(x => x.NewTimeSlotStart)
            .WithMessage("'NewTimeSlotEnd' must be later than 'NewTimeSlotStart'.");
    }
}

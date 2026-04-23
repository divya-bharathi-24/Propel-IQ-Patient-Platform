using FluentValidation;
using Propel.Modules.Appointment.Commands;

namespace Propel.Modules.Appointment.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateBookingCommand"/> (US_019, task_002; US_023, AC-1).
/// <list type="bullet">
///   <item><c>SlotSpecialtyId</c> — must be a non-empty GUID.</item>
///   <item><c>SlotDate</c> — must be today (UTC) or a future date.</item>
///   <item><c>SlotTimeStart</c> — must not be empty (default TimeOnly).</item>
///   <item><c>IntakeMode</c> — must be a valid enum value.</item>
///   <item><c>InsuranceName</c> and <c>InsuranceId</c> are optional — validated as soft-check only.</item>
///   <item><c>PreferredDate</c> — when present, must be today or a future date (US_023, AC-1).</item>
///   <item><c>PreferredTimeSlot</c> — required when <c>PreferredDate</c> is provided (US_023, AC-1).</item>
/// </list>
/// <c>patientId</c> is intentionally absent from this validator; it is resolved from JWT claims
/// inside the handler and never from the request body (OWASP A01).
/// </summary>
public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.SlotSpecialtyId)
            .NotEmpty()
            .WithMessage("'SlotSpecialtyId' must not be empty.");

        RuleFor(x => x.SlotDate)
            .NotEmpty()
            .Must(date => date >= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("'SlotDate' must be today or a future date.");

        RuleFor(x => x.SlotTimeStart)
            .NotEmpty()
            .WithMessage("'SlotTimeStart' must not be empty.");

        RuleFor(x => x.IntakeMode)
            .IsInEnum()
            .WithMessage("'IntakeMode' must be a valid value.");

        // US_023, AC-1 — preferred slot is optional; when provided both fields are required.
        When(x => x.PreferredDate.HasValue, () =>
        {
            RuleFor(x => x.PreferredDate!.Value)
                .Must(date => date >= DateOnly.FromDateTime(DateTime.UtcNow.Date))
                .WithMessage("'PreferredDate' must be today or a future date.");

            RuleFor(x => x.PreferredTimeSlot)
                .NotNull()
                .WithMessage("'PreferredTimeSlot' is required when 'PreferredDate' is provided.");
        });
    }
}

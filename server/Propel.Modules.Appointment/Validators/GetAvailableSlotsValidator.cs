using FluentValidation;
using Propel.Modules.Appointment.Queries;

namespace Propel.Modules.Appointment.Validators;

/// <summary>
/// FluentValidation validator for <see cref="GetAvailableSlotsQuery"/> (US_018, task_002).
/// <list type="bullet">
///   <item><c>SpecialtyId</c> — must be a non-empty GUID.</item>
///   <item><c>Date</c> — must be today (UTC) or a future date; past dates are rejected
///         with HTTP 400 because no future slots exist for them.</item>
/// </list>
/// Registered automatically via <c>AddValidatorsFromAssemblies</c> in <c>Program.cs</c>
/// and executed by the MediatR <c>ValidationBehavior</c> pipeline before the handler runs.
/// </summary>
public sealed class GetAvailableSlotsValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    public GetAvailableSlotsValidator()
    {
        RuleFor(x => x.SpecialtyId)
            .NotEmpty()
            .WithMessage("'SpecialtyId' must not be empty.");

        RuleFor(x => x.Date)
            .NotEmpty()
            .Must(date => date >= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("'Date' must be today or a future date.");
    }
}

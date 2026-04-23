using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="SaveIntakeDraftCommand"/> (US_017, AC-3, AC-4).
/// <para>
/// Validates that the draft payload is structurally sound before the autosave is persisted.
/// These rules produce a standard HTTP 400 response via <c>GlobalExceptionFilter</c>.
/// </para>
/// </summary>
public sealed class SaveDraftRequestValidator : AbstractValidator<SaveIntakeDraftCommand>
{
    public SaveDraftRequestValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("appointmentId must not be empty.");

        RuleFor(x => x.DraftData)
            .NotNull().WithMessage("draftData is required.");
    }
}

using FluentValidation;

namespace Propel.Api.Gateway.Features.Documents.UploadStaffClinicalNote;

/// <summary>
/// FluentValidation rules for <see cref="UploadStaffClinicalNoteCommand"/> (US_039, AC-1, TR-020).
/// Enforced by the MediatR <c>ValidationBehavior&lt;,&gt;</c> pipeline before the handler executes.
/// Validation failures produce HTTP 400 via <c>GlobalExceptionFilter</c>.
/// </summary>
public sealed class UploadStaffClinicalNoteCommandValidator
    : AbstractValidator<UploadStaffClinicalNoteCommand>
{
    /// <summary>Maximum accepted file size: 25 MB = 26,214,400 bytes (FR-042).</summary>
    private const long MaxFileSizeBytes = 26_214_400L;

    public UploadStaffClinicalNoteCommandValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("PatientId is required.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("A PDF file is required.")
            .DependentRules(() =>
            {
                RuleFor(x => x.File.ContentType)
                    .Equal("application/pdf").WithMessage("Only PDF files are accepted.");

                RuleFor(x => x.File.Length)
                    .LessThanOrEqualTo(MaxFileSizeBytes)
                    .WithMessage("File too large — maximum 25 MB.");
            });

        // EncounterReference is optional; when provided it must not exceed 100 chars (AC-1).
        RuleFor(x => x.EncounterReference)
            .MaximumLength(100)
            .WithMessage("EncounterReference must not exceed 100 characters.")
            .When(x => x.EncounterReference is not null);
    }
}

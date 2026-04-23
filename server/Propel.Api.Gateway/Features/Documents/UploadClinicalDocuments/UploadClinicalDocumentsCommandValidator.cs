using FluentValidation;

namespace Propel.Api.Gateway.Features.Documents.UploadClinicalDocuments;

/// <summary>
/// Batch-level FluentValidation rules for <see cref="UploadClinicalDocumentsCommand"/> (US_038, AC-2, TR-020).
/// Enforced by the MediatR <c>ValidationBehavior&lt;,&gt;</c> pipeline before the handler executes.
/// <para>
/// Validates batch-level constraints only (presence of files, max 20 per batch — FR-042).
/// Per-file validation (PDF MIME, 25 MB cap, magic bytes) is handled inline in the handler
/// to support partial batch semantics — individual file errors do NOT abort the entire batch (AC-4).
/// </para>
/// </summary>
public sealed class UploadClinicalDocumentsCommandValidator
    : AbstractValidator<UploadClinicalDocumentsCommand>
{
    /// <summary>Maximum files accepted per batch request (FR-042).</summary>
    private const int MaxFilesPerBatch = 20;

    public UploadClinicalDocumentsCommandValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("PatientId is required.");

        RuleFor(x => x.Files)
            .NotEmpty().WithMessage("At least one file is required.")
            .Must(files => files.Count <= MaxFilesPerBatch)
            .WithMessage($"Maximum {MaxFilesPerBatch} files per upload batch.");
    }
}

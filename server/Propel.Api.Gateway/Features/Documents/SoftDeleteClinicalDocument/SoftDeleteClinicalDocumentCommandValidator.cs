using FluentValidation;

namespace Propel.Api.Gateway.Features.Documents.SoftDeleteClinicalDocument;

/// <summary>
/// FluentValidation rules for <see cref="SoftDeleteClinicalDocumentCommand"/> (US_039, TR-020).
/// Enforced by the MediatR <c>ValidationBehavior&lt;,&gt;</c> pipeline before the handler executes.
/// </summary>
public sealed class SoftDeleteClinicalDocumentCommandValidator
    : AbstractValidator<SoftDeleteClinicalDocumentCommand>
{
    public SoftDeleteClinicalDocumentCommandValidator()
    {
        RuleFor(x => x.DocumentId)
            .NotEmpty().WithMessage("DocumentId is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A deletion reason is required.")
            .MinimumLength(10).WithMessage("Deletion reason must be at least 10 characters.")
            .MaximumLength(500).WithMessage("Deletion reason must not exceed 500 characters.");
    }
}

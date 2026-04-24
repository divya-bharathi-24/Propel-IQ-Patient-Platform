using FluentValidation;

namespace Propel.Api.Gateway.Features.MedicalCoding;

/// <summary>
/// FluentValidation rules for <see cref="GetMedicalCodeSuggestionsQuery"/> (AC-1, NFR-006).
/// Enforced by the MediatR <c>ValidationBehavior&lt;,&gt;</c> pipeline before the handler executes;
/// a failed rule produces HTTP 400 via <c>ExceptionHandlingMiddleware</c>.
/// </summary>
public sealed class GetMedicalCodeSuggestionsQueryValidator
    : AbstractValidator<GetMedicalCodeSuggestionsQuery>
{
    public GetMedicalCodeSuggestionsQueryValidator()
    {
        RuleFor(q => q.PatientId)
            .NotEmpty()
            .WithMessage("PatientId must be a valid non-empty GUID.");
    }
}

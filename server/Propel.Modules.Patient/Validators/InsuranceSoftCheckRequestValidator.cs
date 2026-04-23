using FluentValidation;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RunInsuranceSoftCheckQuery"/> (US_022, task_002, NFR-014).
/// <para>
/// No required-field rules are defined — missing <c>ProviderName</c> or <c>InsuranceId</c>
/// is a valid input that the classification logic handles as <c>Incomplete</c> (AC-1, AC-4).
/// Only max-length constraints are enforced to prevent oversized inputs from reaching the DB
/// (input sanitization, NFR-014).
/// </para>
/// </summary>
public sealed class InsuranceSoftCheckRequestValidator : AbstractValidator<RunInsuranceSoftCheckQuery>
{
    public InsuranceSoftCheckRequestValidator()
    {
        RuleFor(x => x.ProviderName)
            .MaximumLength(200)
            .WithMessage("ProviderName must not exceed 200 characters.")
            .When(x => x.ProviderName is not null);

        RuleFor(x => x.InsuranceId)
            .MaximumLength(100)
            .WithMessage("InsuranceId must not exceed 100 characters.")
            .When(x => x.InsuranceId is not null);
    }
}

using FluentValidation;
using Propel.Modules.Clinical.Queries;

namespace Propel.Modules.Clinical.Validators;

/// <summary>
/// FluentValidation validator for <see cref="GetPatientConflictsQuery"/>
/// (EP-008-II/us_044, task_003, AC-4).
/// <list type="bullet">
///   <item><c>PatientId</c> must be a non-empty GUID — returns HTTP 400 if invalid.</item>
/// </list>
/// </summary>
public sealed class GetPatientConflictsQueryValidator : AbstractValidator<GetPatientConflictsQuery>
{
    public GetPatientConflictsQueryValidator()
    {
        RuleFor(q => q.PatientId)
            .NotEmpty()
            .WithMessage("PatientId must be a valid non-empty GUID.");
    }
}

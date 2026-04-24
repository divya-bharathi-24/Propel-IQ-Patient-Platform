using FluentValidation;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Validators;

/// <summary>
/// FluentValidation validator for <see cref="ResolveConflictCommand"/>
/// (EP-008-II/us_044, task_003, AC-3).
/// <list type="bullet">
///   <item><c>ConflictId</c> must be a non-empty GUID.</item>
///   <item><c>StaffUserId</c> must be a non-empty GUID (sourced from JWT — validated here as a safety net).</item>
///   <item><c>ResolvedValue</c> is required and must not exceed 1,000 characters.</item>
///   <item><c>ResolutionNote</c>, when provided, must not exceed 2,000 characters.</item>
/// </list>
/// </summary>
public sealed class ResolveConflictCommandValidator : AbstractValidator<ResolveConflictCommand>
{
    public ResolveConflictCommandValidator()
    {
        RuleFor(c => c.ConflictId)
            .NotEmpty()
            .WithMessage("ConflictId must be a valid non-empty GUID.");

        RuleFor(c => c.StaffUserId)
            .NotEmpty()
            .WithMessage("StaffUserId must be a valid non-empty GUID.");

        RuleFor(c => c.ResolvedValue)
            .NotEmpty()
            .WithMessage("ResolvedValue is required.")
            .MaximumLength(1000)
            .WithMessage("ResolvedValue must not exceed 1,000 characters.");

        RuleFor(c => c.ResolutionNote)
            .MaximumLength(2000)
            .WithMessage("ResolutionNote must not exceed 2,000 characters.")
            .When(c => c.ResolutionNote is not null);
    }
}

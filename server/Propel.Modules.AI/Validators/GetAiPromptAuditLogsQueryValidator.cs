using FluentValidation;
using Propel.Modules.AI.Queries;

namespace Propel.Modules.AI.Validators;

/// <summary>
/// FluentValidation validator for <see cref="GetAiPromptAuditLogsQuery"/> (EP-010/us_049, AC-4, task_002).
/// All filter parameters are optional. Page size is fixed at 50 and is NOT configurable by callers.
/// </summary>
public sealed class GetAiPromptAuditLogsQueryValidator : AbstractValidator<GetAiPromptAuditLogsQuery>
{
    public GetAiPromptAuditLogsQueryValidator()
    {
        RuleFor(x => x.UserId)
            .MaximumLength(100)
            .When(x => x.UserId is not null)
            .WithMessage("'userId' must not exceed 100 characters.");

        RuleFor(x => x.SessionId)
            .MaximumLength(200)
            .When(x => x.SessionId is not null)
            .WithMessage("'sessionId' must not exceed 200 characters.");
    }
}

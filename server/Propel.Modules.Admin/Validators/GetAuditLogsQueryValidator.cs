using FluentValidation;
using Propel.Modules.Admin.Queries;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="GetAuditLogsQuery"/> (US_047, AC-2).
/// Validates cross-property date range constraint: <c>dateFrom</c> must not be after <c>dateTo</c>.
/// Page size is fixed at 50 and is NOT configurable by callers — no validation rule needed.
/// </summary>
public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.DateFrom)
            .LessThanOrEqualTo(x => x.DateTo)
            .When(x => x.DateFrom.HasValue && x.DateTo.HasValue)
            .WithMessage("'dateFrom' must be earlier than or equal to 'dateTo'.");
    }
}

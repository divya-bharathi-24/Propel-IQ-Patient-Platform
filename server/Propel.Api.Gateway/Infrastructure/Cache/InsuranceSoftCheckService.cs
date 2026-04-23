using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Enums;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.Cache;

/// <summary>
/// EF Core implementation of <see cref="IInsuranceSoftCheckService"/> (US_019, task_002).
/// Queries the <c>DummyInsurers</c> seed table with a case-insensitive match on
/// <c>InsurerName</c> and an <c>InsuranceId.StartsWith(MemberIdPrefix)</c> check.
/// Any <see cref="Exception"/> from the database layer swallows silently and returns
/// <see cref="InsuranceValidationResult.CheckPending"/> so booking always proceeds (NFR-018, FR-040).
/// </summary>
public sealed class InsuranceSoftCheckService : IInsuranceSoftCheckService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<InsuranceSoftCheckService> _logger;

    public InsuranceSoftCheckService(AppDbContext dbContext, ILogger<InsuranceSoftCheckService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<InsuranceValidationResult> CheckAsync(
        string? insuranceName,
        string? insuranceId,
        CancellationToken cancellationToken = default)
    {
        // Missing fields → Incomplete; booking proceeds (FR-040).
        if (string.IsNullOrWhiteSpace(insuranceName) || string.IsNullOrWhiteSpace(insuranceId))
        {
            _logger.LogDebug(
                "InsuranceSoftCheck_Incomplete: InsuranceName={InsuranceName} InsuranceId={InsuranceId}",
                insuranceName, insuranceId);
            return InsuranceValidationResult.Incomplete;
        }

        try
        {
            var normalizedName = insuranceName.Trim().ToUpperInvariant();
            var normalizedId = insuranceId.Trim().ToUpperInvariant();

            // Case-insensitive match: InsurerName matches AND InsuranceId starts with MemberIdPrefix.
            var matched = await _dbContext.DummyInsurers
                .Where(d => d.IsActive
                    && d.InsurerName.ToUpper() == normalizedName
                    && normalizedId.StartsWith(d.MemberIdPrefix.ToUpper()))
                .AnyAsync(cancellationToken);

            var result = matched
                ? InsuranceValidationResult.Verified
                : InsuranceValidationResult.NotRecognized;

            _logger.LogDebug(
                "InsuranceSoftCheck_{Result}: InsuranceName={InsuranceName}",
                result, insuranceName);

            return result;
        }
        catch (Exception ex)
        {
            // Graceful degradation: any DB failure must not block the booking (NFR-018, FR-040).
            _logger.LogWarning(
                ex,
                "InsuranceSoftCheck_CheckPending: DummyInsurers query failed for InsuranceName={InsuranceName}",
                insuranceName);
            return InsuranceValidationResult.CheckPending;
        }
    }
}

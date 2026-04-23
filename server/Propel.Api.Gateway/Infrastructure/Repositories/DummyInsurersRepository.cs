using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDummyInsurersRepository"/> (US_022, task_002).
/// Performs a case-insensitive match against the <c>DummyInsurers</c> seed table.
/// <para>
/// Matching rule: <c>InsurerName</c> must equal <paramref name="providerName"/> (case-insensitive)
/// AND the patient's <paramref name="insuranceId"/> must start with the record's
/// <c>MemberIdPrefix</c> (case-insensitive). This mirrors the approach used by
/// <c>InsuranceSoftCheckService</c> in the booking flow (US_019, task_002).
/// </para>
/// <para>
/// The dataset is small (≤ 20 rows) so no pagination is needed.
/// </para>
/// </summary>
public sealed class DummyInsurersRepository : IDummyInsurersRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<DummyInsurersRepository> _logger;

    public DummyInsurersRepository(AppDbContext db, ILogger<DummyInsurersRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        string providerName,
        string insuranceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = providerName.Trim().ToUpperInvariant();
        var normalizedId   = insuranceId.Trim().ToUpperInvariant();

        var exists = await _db.DummyInsurers
            .Where(d => d.IsActive
                && d.InsurerName.ToUpper() == normalizedName
                && normalizedId.StartsWith(d.MemberIdPrefix.ToUpper()))
            .AnyAsync(cancellationToken);

        _logger.LogDebug(
            "DummyInsurersRepository_ExistsAsync: Match={Exists}",
            exists);

        return exists;
    }
}

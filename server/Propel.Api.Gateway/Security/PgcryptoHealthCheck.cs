using Microsoft.Extensions.Diagnostics.HealthChecks;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Security;

/// <summary>
/// Health check that confirms the pgcrypto PostgreSQL extension is active.
/// Registered at <c>/health</c> and <c>/healthz</c>; fails the liveness probe
/// when pgcrypto is absent, preventing the app from accepting traffic in a broken state.
/// </summary>
internal sealed class PgcryptoHealthCheck : IHealthCheck
{
    private readonly IEncryptionService _encryption;

    public PgcryptoHealthCheck(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = _encryption.IsAvailable
            ? HealthCheckResult.Healthy("pgcrypto extension is active.")
            : HealthCheckResult.Unhealthy(
                "pgcrypto extension is NOT available. " +
                "Run: CREATE EXTENSION IF NOT EXISTS pgcrypto;");

        return Task.FromResult(result);
    }
}

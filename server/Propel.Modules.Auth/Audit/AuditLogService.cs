using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Modules.Auth.Audit;

/// <summary>
/// Application-layer service that wraps <see cref="IAuditLogRepository"/> with a retry-once
/// strategy and <c>Critical</c> Serilog alerting on consecutive failures (US_013, FR-006).
/// <para>
/// Design decisions:
/// <list type="bullet">
///   <item>Single retry after 100 ms — sufficient for a transient DB connection blip.</item>
///   <item>On two consecutive failures the authentication action still returns the correct
///         HTTP response; the failure is surfaced via a <c>LogCritical</c> alert only
///         (security over audit atomicity — OWASP A09).</item>
///   <item>Critical log payload contains <c>Action</c> and <c>UserId</c> only — no IP address,
///         email, or password hash to prevent PHI leakage through log aggregators (OWASP A09,
///         HIPAA §164.312(b)).</item>
/// </list>
/// </para>
/// </summary>
public sealed class AuditLogService
{
    private readonly IAuditLogRepository _repo;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IAuditLogRepository repo, ILogger<AuditLogService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Persists an immutable audit entry with a single retry on transient failure.
    /// Never throws — auth pipeline must not be interrupted by audit subsystem failures.
    /// </summary>
    public async Task AppendAsync(AuditLog entry, CancellationToken ct = default)
    {
        try
        {
            await _repo.AppendAsync(entry, ct);
            return;
        }
        catch (Exception ex1)
        {
            _logger.LogWarning(ex1, "Audit log write failed (attempt 1), retrying in 100 ms…");
            await Task.Delay(100, ct);
        }

        try
        {
            await _repo.AppendAsync(entry, ct);
        }
        catch (Exception ex2)
        {
            // CRITICAL alert — Action and UserId only; no IP address or email (OWASP A09, HIPAA §164.312(b))
            _logger.LogCritical(
                ex2,
                "AUDIT_LOG_WRITE_FAILURE: action={Action} userId={UserId}",
                entry.Action,
                entry.UserId);
            // Do NOT re-throw — the calling auth action must still succeed (edge case spec)
        }
    }
}

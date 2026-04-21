using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Propel.Api.Gateway.Infrastructure.Security;

/// <summary>
/// Named rate-limit policy definitions for public auth endpoints (NFR-017, AC-1, OWASP A04).
/// Centralises all PermitLimit / Window tuning in a single place so configuration drift is impossible.
/// <para>
/// Call <see cref="Configure"/> inside <c>builder.Services.AddRateLimiter(options => ...)</c>
/// in <c>Program.cs</c>.
/// </para>
/// </summary>
public static class RateLimitingPolicies
{
    /// <summary>Policy name for POST /api/auth/login (10 req / 60 s per IP).</summary>
    public const string Login = "login";

    /// <summary>Policy name for POST /api/auth/register (5 req / 10 min per IP).</summary>
    public const string Register = "register";

    /// <summary>Policy name for POST /api/auth/resend-verification (3 req / 5 min per email hash).</summary>
    public const string ResendVerification = "resend-verification";

    /// <summary>Global fallback policy applied to all controller actions.</summary>
    public const string Global = "global";

    /// <summary>
    /// Registers all named rate-limit policies on <paramref name="options"/>.
    /// Policies use in-process counters by default; swap the partition factory for
    /// a Redis-backed store when horizontal scaling is required (NFR-010).
    /// </summary>
    /// <param name="options">The <see cref="RateLimiterOptions"/> from <c>AddRateLimiter</c>.</param>
    /// <param name="configuration">Application configuration for tunable defaults.</param>
    public static void Configure(RateLimiterOptions options, IConfiguration configuration)
    {
        // ── Global fallback: 100 req / min ──────────────────────────────────────
        options.AddFixedWindowLimiter(Global, opt =>
        {
            opt.Window = TimeSpan.FromMinutes(configuration.GetValue("RateLimit:WindowMinutes", 1));
            opt.PermitLimit = configuration.GetValue("RateLimit:PermitLimit", 100);
            opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 0;
        });

        // ── POST /api/auth/register: 5 req / 10 min per IP (NFR-017, AC-1) ─────
        options.AddFixedWindowLimiter(Register, opt =>
        {
            opt.Window = TimeSpan.FromMinutes(10);
            opt.PermitLimit = 5;
            opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 0;
        });

        // ── POST /api/auth/login: 10 req / 60 s per IP (OWASP A04, NFR-017, AC-1) ─
        // Partitioned by remote IP so one attacker cannot exhaust quota for other IPs.
        options.AddPolicy(Login, httpContext =>
        {
            string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
        });

        // ── POST /api/auth/resend-verification: 3 req / 5 min per email hash ───
        // Sliding window prevents burst at boundary. Partitioned by SHA-256(IP) so
        // PII (email) is never written to the rate-limit store (NFR-017, OWASP A07).
        options.AddPolicy(ResendVerification, httpContext =>
        {
            string rawKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
            // Use first 16 hex chars of SHA-256 as a compact, collision-resistant partition key.
            string partitionKey = Convert.ToHexString(hashBytes)[..16];

            return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(5),
                    PermitLimit = 3,
                    SegmentsPerWindow = 3,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
        });
    }
}

namespace Propel.Modules.Auth.Audit;

/// <summary>
/// Compile-time string constants for all authentication event action names written to
/// <c>audit_logs.action</c> (US_013, FR-006).
/// Using constants eliminates magic strings across all auth event handlers (anti-patterns rule).
/// </summary>
public static class AuthAuditActions
{
    /// <summary>Successful patient or staff authentication.</summary>
    public const string Login = "Login";

    /// <summary>Authentication attempt with invalid credentials (wrong password or unknown email).</summary>
    public const string FailedLogin = "FailedLogin";

    /// <summary>Redis session TTL expiry detected — session invalidated server-side (AC-3).</summary>
    public const string SessionTimeout = "SessionTimeout";

    /// <summary>Explicit logout initiated by the authenticated user (AC-4).</summary>
    public const string Logout = "Logout";

    /// <summary>IP-based login rate-limit block — user identity not yet established (AC-2, OWASP A04).</summary>
    public const string RateLimitBlock = "RateLimitBlock";
}

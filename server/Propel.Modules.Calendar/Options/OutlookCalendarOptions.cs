namespace Propel.Modules.Calendar.Options;

/// <summary>
/// Strongly-typed configuration options for Microsoft Outlook Calendar OAuth 2.0 integration
/// (us_036, OWASP A02).
/// Bound from the <c>"OutlookCalendar"</c> section via <c>IOptions&lt;OutlookCalendarOptions&gt;</c>.
/// <c>ClientSecret</c> MUST be sourced from Azure Key Vault or environment variable at runtime —
/// never stored in <c>appsettings.json</c> (OWASP A02).
/// </summary>
public sealed class OutlookCalendarOptions
{
    /// <summary>Azure AD application (client) ID — non-secret, safe in appsettings.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD client secret.
    /// MUST be loaded from the <c>OUTLOOK_CLIENT_SECRET</c> environment variable at runtime.
    /// This property is populated by the options binder; ensure the env var is set.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Azure AD tenant ID or "common" for multi-tenant apps.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Registered redirect URI — must exactly match the Azure App Registration entry.
    /// Example: <c>https://api.propeliq.app/api/calendar/outlook/callback</c>
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Microsoft Graph scopes requested during OAuth consent (AC-1).</summary>
    public string[] Scopes { get; set; } = ["Calendars.ReadWrite", "offline_access"];

    /// <summary>
    /// Frontend base URL to redirect the patient after OAuth completes.
    /// Query-string result is appended: <c>?calendarResult=success|failed|revoked</c>
    /// </summary>
    public string FrontendConfirmationUrl { get; set; } = string.Empty;
}

namespace Propel.Modules.Calendar.Options;

/// <summary>
/// Non-secret Google OAuth 2.0 settings bound from <c>appsettings.json</c> section
/// <c>"GoogleCalendar"</c> (us_035, OWASP A02).
/// <c>GOOGLE_CLIENT_SECRET</c> is intentionally absent — loaded exclusively from
/// <c>Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")</c> at runtime.
/// </summary>
public sealed class GoogleCalendarSettings
{
    /// <summary>Google OAuth 2.0 client ID (non-secret, safe in appsettings).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Registered redirect URI — must exactly match the Google Cloud Console entry.
    /// Example: <c>https://api.propeliq.app/api/calendar/google/callback</c>
    /// </summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>
    /// Frontend base URL to redirect the patient after OAuth completes.
    /// Query-string result is appended: <c>?calendarResult=success|declined|failed|expired</c>
    /// </summary>
    public string FrontendConfirmationUrl { get; set; } = "";
}

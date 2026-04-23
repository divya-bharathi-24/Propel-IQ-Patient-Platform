using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Interfaces;
using Propel.Modules.Calendar.Options;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Decrypts, caches, and refreshes OAuth 2.0 access tokens for Google and Outlook calendar
/// providers (us_037, EC-1, OWASP A02).
/// <para>
/// Tokens are stored encrypted in <c>PatientOAuthToken</c> via ASP.NET Core Data Protection
/// (AES-256). This service decrypts them in-process and never writes plaintext to logs or DB.
/// </para>
/// <para>
/// <b>Refresh strategy</b>: proactively refreshes if the token expires within 60 seconds.
/// On explicit 401 from the adapter, the caller (CalendarPropagationService) calls
/// <see cref="RefreshTokenAsync"/> and retries the API call once (EC-1).
/// </para>
/// </summary>
public sealed class OAuthTokenService : IOAuthTokenService
{
    private const string DataProtectionPurpose = "GoogleOAuthTokens.v1";
    private const string GoogleTokenEndpoint   = "https://oauth2.googleapis.com/token";

    private readonly IPatientOAuthTokenRepository _oauthTokenRepo;
    private readonly IDataProtector               _protector;
    private readonly IHttpClientFactory            _httpClientFactory;
    private readonly GoogleCalendarSettings        _googleSettings;
    private readonly OutlookCalendarOptions        _outlookOptions;
    private readonly ILogger<OAuthTokenService>    _logger;

    public OAuthTokenService(
        IPatientOAuthTokenRepository oauthTokenRepo,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleCalendarSettings> googleSettings,
        IOptions<OutlookCalendarOptions> outlookOptions,
        ILogger<OAuthTokenService> logger)
    {
        _oauthTokenRepo = oauthTokenRepo;
        _protector      = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _httpClientFactory = httpClientFactory;
        _googleSettings = googleSettings.Value;
        _outlookOptions = outlookOptions.Value;
        _logger         = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetAccessTokenAsync(
        Guid patientId,
        CalendarProvider provider,
        CancellationToken cancellationToken = default)
    {
        var providerName = provider.ToString();
        var token = await _oauthTokenRepo.GetAsync(patientId, providerName, cancellationToken);
        if (token is null)
        {
            _logger.LogWarning(
                "OAuthTokenService: No token record found for PatientId={PatientId} Provider={Provider}",
                patientId, providerName);
            return null;
        }

        // Proactively refresh if expiring within 60 seconds (EC-1 prevention)
        if (token.ExpiresAt <= DateTime.UtcNow.AddSeconds(60))
        {
            _logger.LogInformation(
                "OAuthTokenService: Token near expiry for PatientId={PatientId} Provider={Provider} — refreshing proactively",
                patientId, providerName);
            return await RefreshTokenAsync(patientId, provider, cancellationToken);
        }

        return _protector.Unprotect(token.EncryptedAccessToken);
    }

    /// <inheritdoc />
    public async Task<string?> RefreshTokenAsync(
        Guid patientId,
        CalendarProvider provider,
        CancellationToken cancellationToken = default)
    {
        var providerName = provider.ToString();
        var token = await _oauthTokenRepo.GetAsync(patientId, providerName, cancellationToken);
        if (token is null)
        {
            _logger.LogWarning(
                "OAuthTokenService: No token record to refresh for PatientId={PatientId} Provider={Provider}",
                patientId, providerName);
            return null;
        }

        return provider switch
        {
            CalendarProvider.Google  => await RefreshGoogleTokenAsync(token, cancellationToken),
            CalendarProvider.Outlook => await RefreshOutlookTokenAsync(token, cancellationToken),
            _ => null
        };
    }

    // ── Google refresh ─────────────────────────────────────────────────────────

    private async Task<string?> RefreshGoogleTokenAsync(
        PatientOAuthToken token,
        CancellationToken cancellationToken)
    {
        var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError(
                "OAuthTokenService: GOOGLE_CLIENT_SECRET environment variable is not set. Cannot refresh token for PatientId={PatientId}",
                token.PatientId);
            return null;
        }

        try
        {
            var refreshToken = _protector.Unprotect(token.EncryptedRefreshToken);
            var httpClient   = _httpClientFactory.CreateClient();

            var response = await httpClient.PostAsync(
                GoogleTokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"]     = _googleSettings.ClientId,
                    ["client_secret"] = clientSecret,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"]    = "refresh_token"
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OAuthTokenService: Google token refresh failed with HTTP {Status} for PatientId={PatientId}",
                    response.StatusCode, token.PatientId);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
                cancellationToken: cancellationToken);

            if (json is null || !json.TryGetValue("access_token", out var at))
            {
                _logger.LogWarning(
                    "OAuthTokenService: Missing access_token in Google refresh response for PatientId={PatientId}",
                    token.PatientId);
                return null;
            }

            var newAccessToken  = at.ToString()!;
            var expiresInSeconds = json.TryGetValue("expires_in", out var exp)
                ? int.Parse(exp.ToString()!)
                : 3600;

            // Persist updated encrypted token (OWASP A02 — never store plaintext)
            token.EncryptedAccessToken = _protector.Protect(newAccessToken);
            token.ExpiresAt            = DateTime.UtcNow.AddSeconds(expiresInSeconds);
            token.UpdatedAt            = DateTime.UtcNow;
            await _oauthTokenRepo.UpsertAsync(token, cancellationToken);

            _logger.LogInformation(
                "OAuthTokenService: Google token refreshed successfully for PatientId={PatientId}",
                token.PatientId);

            return newAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OAuthTokenService: Unexpected error refreshing Google token for PatientId={PatientId}",
                token.PatientId);
            return null;
        }
    }

    // ── Outlook (Microsoft) refresh ────────────────────────────────────────────

    private async Task<string?> RefreshOutlookTokenAsync(
        PatientOAuthToken token,
        CancellationToken cancellationToken)
    {
        var clientSecret = Environment.GetEnvironmentVariable("OUTLOOK_CLIENT_SECRET");
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError(
                "OAuthTokenService: OUTLOOK_CLIENT_SECRET environment variable is not set. Cannot refresh token for PatientId={PatientId}",
                token.PatientId);
            return null;
        }

        var tenantId     = _outlookOptions.TenantId;
        var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

        try
        {
            var refreshToken = _protector.Unprotect(token.EncryptedRefreshToken);
            var httpClient   = _httpClientFactory.CreateClient();

            var scopes = string.Join(" ", _outlookOptions.Scopes);

            var response = await httpClient.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"]     = _outlookOptions.ClientId,
                    ["client_secret"] = clientSecret,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"]    = "refresh_token",
                    ["scope"]         = scopes
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OAuthTokenService: Outlook token refresh failed with HTTP {Status} for PatientId={PatientId}",
                    response.StatusCode, token.PatientId);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
                cancellationToken: cancellationToken);

            if (json is null || !json.TryGetValue("access_token", out var at))
            {
                _logger.LogWarning(
                    "OAuthTokenService: Missing access_token in Outlook refresh response for PatientId={PatientId}",
                    token.PatientId);
                return null;
            }

            var newAccessToken   = at.ToString()!;
            var expiresInSeconds = json.TryGetValue("expires_in", out var exp)
                ? int.Parse(exp.ToString()!)
                : 3600;

            // Persist updated encrypted token (OWASP A02 — never store plaintext)
            token.EncryptedAccessToken = _protector.Protect(newAccessToken);
            token.ExpiresAt            = DateTime.UtcNow.AddSeconds(expiresInSeconds);
            token.UpdatedAt            = DateTime.UtcNow;

            // If the response includes a new refresh token, update it too
            if (json.TryGetValue("refresh_token", out var rt))
                token.EncryptedRefreshToken = _protector.Protect(rt.ToString()!);

            await _oauthTokenRepo.UpsertAsync(token, cancellationToken);

            _logger.LogInformation(
                "OAuthTokenService: Outlook token refreshed successfully for PatientId={PatientId}",
                token.PatientId);

            return newAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OAuthTokenService: Unexpected error refreshing Outlook token for PatientId={PatientId}",
                token.PatientId);
            return null;
        }
    }
}

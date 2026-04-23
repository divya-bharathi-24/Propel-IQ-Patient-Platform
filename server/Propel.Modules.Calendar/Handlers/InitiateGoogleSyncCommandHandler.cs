using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Interfaces;
using Propel.Modules.Calendar.Options;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="InitiateGoogleSyncCommand"/> for <c>GET /api/calendar/google/auth</c>
/// (us_035, AC-1).
/// <list type="number">
///   <item>Resolve <c>patientId</c> from JWT claims (OWASP A01).</item>
///   <item>Validate appointment belongs to the requesting patient.</item>
///   <item>Generate PKCE <c>code_verifier</c> and <c>code_challenge</c> (S256).</item>
///   <item>Generate anti-CSRF <c>state = "{guid}:{appointmentId}"</c>.</item>
///   <item>Store state payload in <see cref="IOAuthStateService"/> with 10-min TTL (OWASP A07).</item>
///   <item>Return the Google authorization URL for a 302 redirect.</item>
/// </list>
/// </summary>
public sealed class InitiateGoogleSyncCommandHandler
    : IRequestHandler<InitiateGoogleSyncCommand, string>
{
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly IOAuthStateService _stateService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GoogleCalendarSettings _settings;
    private readonly ILogger<InitiateGoogleSyncCommandHandler> _logger;

    public InitiateGoogleSyncCommandHandler(
        IAppointmentBookingRepository appointmentRepo,
        IOAuthStateService stateService,
        IHttpContextAccessor httpContextAccessor,
        IOptions<GoogleCalendarSettings> settings,
        ILogger<InitiateGoogleSyncCommandHandler> logger)
    {
        _appointmentRepo      = appointmentRepo;
        _stateService         = stateService;
        _httpContextAccessor  = httpContextAccessor;
        _settings             = settings.Value;
        _logger               = logger;
    }

    public async Task<string> Handle(
        InitiateGoogleSyncCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: resolve patientId from JWT claims (OWASP A01) ─────────────
        var patientIdStr = _httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Patient identity could not be resolved from JWT.");

        var patientId = Guid.Parse(patientIdStr);

        // ── Step 2: verify appointment ownership ──────────────────────────────
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(
            request.AppointmentId, cancellationToken);

        if (appointment is null || appointment.PatientId != patientId)
            throw new UnauthorizedAccessException(
                $"Appointment {request.AppointmentId} does not belong to patient {patientId}.");

        // ── Step 3: PKCE code_verifier + code_challenge ───────────────────────
        var codeVerifier  = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // ── Step 4: anti-CSRF state parameter ────────────────────────────────
        var stateKey = $"{Guid.NewGuid()}:{request.AppointmentId}";

        // ── Step 5: store PKCE state (10-min TTL, one-time-use) ───────────────
        var payload = JsonSerializer.Serialize(new
        {
            codeVerifier,
            patientId = patientId.ToString(),
            appointmentId = request.AppointmentId.ToString()
        });
        await _stateService.SetAsync(stateKey, payload, cancellationToken);

        _logger.LogInformation(
            "Google OAuth initiated for PatientId={PatientId} AppointmentId={AppointmentId}",
            patientId, request.AppointmentId);

        // ── Step 6: build Google authorization URL ────────────────────────────
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"]             = _settings.ClientId,
            ["redirect_uri"]          = _settings.RedirectUri,
            ["response_type"]         = "code",
            ["scope"]                 = "https://www.googleapis.com/auth/calendar.events",
            ["access_type"]           = "offline",
            ["prompt"]                = "consent",
            ["state"]                 = stateKey,
            ["code_challenge"]        = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var queryString = string.Join("&",
            queryParams.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"https://accounts.google.com/o/oauth2/v2/auth?{queryString}";
    }

    // ── PKCE helpers ──────────────────────────────────────────────────────────

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

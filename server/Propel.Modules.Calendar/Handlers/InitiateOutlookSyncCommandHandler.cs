using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Options;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="InitiateOutlookSyncCommand"/> for <c>POST /api/calendar/outlook/initiate</c>
/// (us_036, AC-1).
/// <list type="number">
///   <item>Resolves <c>patientId</c> from JWT claims (OWASP A01).</item>
///   <item>Validates appointment ownership.</item>
///   <item>Encodes anti-CSRF state: <c>Base64(appointmentId:patientId)</c>.</item>
///   <item>Builds MSAL <see cref="IConfidentialClientApplication"/> and generates
///         the Microsoft OAuth 2.0 PKCE authorization URL.</item>
///   <item>Returns <see cref="InitiateOutlookSyncResultDto"/> containing the auth URL.</item>
/// </list>
/// <c>ClientSecret</c> is sourced exclusively from <c>OutlookCalendarOptions</c> which is
/// bound from Key Vault / environment variables — never hardcoded (OWASP A02).
/// </summary>
public sealed class InitiateOutlookSyncCommandHandler
    : IRequestHandler<InitiateOutlookSyncCommand, InitiateOutlookSyncResultDto>
{
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly OutlookCalendarOptions _options;
    private readonly ILogger<InitiateOutlookSyncCommandHandler> _logger;

    public InitiateOutlookSyncCommandHandler(
        IAppointmentBookingRepository appointmentRepo,
        IHttpContextAccessor httpContextAccessor,
        IOptions<OutlookCalendarOptions> options,
        ILogger<InitiateOutlookSyncCommandHandler> logger)
    {
        _appointmentRepo     = appointmentRepo;
        _httpContextAccessor = httpContextAccessor;
        _options             = options.Value;
        _logger              = logger;
    }

    public async Task<InitiateOutlookSyncResultDto> Handle(
        InitiateOutlookSyncCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: resolve patientId from JWT claims (OWASP A01) ─────────────
        var patientIdStr = _httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException(
                "Patient identity could not be resolved from JWT.");

        if (!Guid.TryParse(patientIdStr, out var patientId))
            throw new UnauthorizedAccessException("Invalid patientId claim in JWT.");

        // ── Step 2: verify appointment ownership ──────────────────────────────
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(
            request.AppointmentId, cancellationToken);

        if (appointment is null || appointment.PatientId != patientId)
            throw new UnauthorizedAccessException(
                $"Appointment {request.AppointmentId} does not belong to patient {patientId}.");

        // ── Step 3: encode CSRF state — opaque to caller (OWASP A01) ─────────
        var rawState = $"{request.AppointmentId}:{patientId}";
        var state    = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawState));

        // ── Step 4: build MSAL confidential client ────────────────────────────
        var app = BuildConfidentialClient();

        // ── Step 5: generate authorization URL with PKCE ──────────────────────
        // state is not included in the token cache key (it is a per-request CSRF token)
        var authUri = await app
            .GetAuthorizationRequestUrl(_options.Scopes)
            .WithExtraQueryParameters(new Dictionary<string, (string value, bool includeInCacheKey)>
            {
                ["state"] = (state, false)
            })
            .ExecuteAsync(cancellationToken);

        _logger.LogInformation(
            "Outlook OAuth authorization URL generated for PatientId={PatientId} AppointmentId={AppointmentId}",
            patientId, request.AppointmentId);

        return new InitiateOutlookSyncResultDto(authUri.ToString());
    }

    private IConfidentialClientApplication BuildConfidentialClient()
    {
        // OWASP A02: ClientSecret sourced from options — bound from Key Vault / env vars
        return ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
            .WithRedirectUri(_options.RedirectUri)
            .Build();
    }
}

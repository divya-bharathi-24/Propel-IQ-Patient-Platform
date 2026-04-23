using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Modules.Appointment.Commands;
using StackExchange.Redis;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="HoldSlotCommand"/> for <c>POST /api/appointments/hold-slot</c>
/// (US_019, AC-2). Writes a Redis key <c>slot_hold:{specialtyId}:{date}:{timeSlot}:{patientId}</c>
/// with a 300-second TTL to reserve the slot during the 5-minute booking wizard window.
/// <para>
/// <c>patientId</c> is always resolved from the JWT <c>NameIdentifier</c> claim — never from
/// the request body (OWASP A01). Redis failures are swallowed and logged; the response still
/// returns HTTP 200 so the wizard is not blocked by cache infrastructure issues (NFR-018).
/// </para>
/// </summary>
public sealed class HoldSlotCommandHandler : IRequestHandler<HoldSlotCommand>
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HoldSlotCommandHandler> _logger;

    private static readonly TimeSpan HoldTtl = TimeSpan.FromSeconds(300);

    public HoldSlotCommandHandler(
        IConnectionMultiplexer redis,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HoldSlotCommandHandler> logger)
    {
        _redis = redis;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task Handle(HoldSlotCommand request, CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT claims — OWASP A01: never from request body.
        var patientIdStr = _httpContextAccessor.HttpContext!.User
            .FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var holdKey =
            $"slot_hold:{request.SpecialtyId}:{request.Date:yyyy-MM-dd}:{request.TimeSlotStart:HH\\:mm}:{patientId}";

        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(holdKey, "1", HoldTtl);

            _logger.LogDebug(
                "SlotHold_Set: Key={HoldKey} TTL={Ttl}s PatientId={PatientId}",
                holdKey, HoldTtl.TotalSeconds, patientId);
        }
        catch (Exception ex)
        {
            // Graceful degradation: Redis unavailability must not fail the wizard step (NFR-018).
            _logger.LogWarning(
                ex,
                "SlotHold_Failed: Could not set hold key {HoldKey} for PatientId={PatientId}",
                holdKey, patientId);
        }
    }
}

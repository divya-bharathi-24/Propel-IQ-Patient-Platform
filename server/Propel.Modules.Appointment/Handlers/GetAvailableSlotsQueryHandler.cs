using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Configuration;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Infrastructure;
using Propel.Modules.Appointment.Queries;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="GetAvailableSlotsQuery"/> for <c>GET /api/appointments/slots</c>
/// (US_018, AC-1 – AC-4, NFR-020).
/// <list type="number">
///   <item><b>Step 1 — Cache read (AC-1, NFR-020)</b>: Attempts a Redis lookup via
///         <see cref="ISlotCacheService.GetAsync"/>. Returns immediately on cache hit.</item>
///   <item><b>Step 2 — DB fallback (AC-3)</b>: On cache miss or Redis failure, queries
///         <see cref="IAppointmentSlotRepository"/> for all <c>Booked</c>/<c>Arrived</c>
///         appointments on the requested date and specialty. The warning event
///         <c>"SlotCache_Miss"</c> is emitted by the cache service itself (AC-3).</item>
///   <item><b>Step 3 — Slot grid generation</b>: Generates the full time-slot grid
///         from <see cref="SlotConfiguration"/> (BusinessHoursStart → BusinessHoursEnd
///         in SlotDurationMinutes increments) and marks each slot's availability.</item>
///   <item><b>Step 4 — Cache write (AC-1)</b>: Stores the computed grid with a 5-second TTL
///         to satisfy the NFR-020 ≤5-second staleness budget. Write failures are swallowed
///         by the cache service (NFR-018 graceful degradation).</item>
/// </list>
/// </summary>
public sealed class GetAvailableSlotsQueryHandler
    : IRequestHandler<GetAvailableSlotsQuery, SlotAvailabilityResponseDto>
{
    private readonly ISlotCacheService _slotCacheService;
    private readonly IAppointmentSlotRepository _slotRepository;
    private readonly IOptions<SlotConfiguration> _slotConfig;
    private readonly ILogger<GetAvailableSlotsQueryHandler> _logger;

    /// <summary>Cache TTL hard-coded to 5 seconds (NFR-020 ≤5-second staleness).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    public GetAvailableSlotsQueryHandler(
        ISlotCacheService slotCacheService,
        IAppointmentSlotRepository slotRepository,
        IOptions<SlotConfiguration> slotConfig,
        ILogger<GetAvailableSlotsQueryHandler> logger)
    {
        _slotCacheService = slotCacheService;
        _slotRepository = slotRepository;
        _slotConfig = slotConfig;
        _logger = logger;
    }

    public async Task<SlotAvailabilityResponseDto> Handle(
        GetAvailableSlotsQuery request,
        CancellationToken cancellationToken)
    {
        var specialtyIdStr = request.SpecialtyId.ToString();

        // Step 1: Redis cache read (AC-1, NFR-020).
        var cachedSlots = await _slotCacheService.GetAsync(specialtyIdStr, request.Date, cancellationToken);
        if (cachedSlots is not null)
        {
            _logger.LogDebug(
                "SlotCache_Hit: SpecialtyId={SpecialtyId} Date={Date} SlotCount={Count}",
                request.SpecialtyId, request.Date, cachedSlots.Count);

            return new SlotAvailabilityResponseDto(request.Date, request.SpecialtyId, cachedSlots);
        }

        // Step 2: DB fallback — query booked/arrived slots for the date (AC-3).
        var bookedSlots = await _slotRepository.GetBookedSlotsAsync(
            request.SpecialtyId, request.Date, cancellationToken);

        // Step 3: Generate full slot grid and mark availability.
        var config = _slotConfig.Value;
        var start = TimeOnly.Parse(config.BusinessHoursStart);
        var end = TimeOnly.Parse(config.BusinessHoursEnd);
        var slotDuration = TimeSpan.FromMinutes(config.SlotDurationMinutes);

        var slots = new List<SlotDto>();
        var current = start;

        while (current.Add(slotDuration) <= end)
        {
            var slotEnd = current.Add(slotDuration);
            // A slot is unavailable if any booked/arrived appointment starts at the same time.
            var isAvailable = !bookedSlots.Any(b => b.TimeSlotStart == current);
            slots.Add(new SlotDto(current, slotEnd, isAvailable));
            current = slotEnd;
        }

        IReadOnlyList<SlotDto> result = slots.AsReadOnly();

        // Step 4: Write to cache (fire-and-forget style — failures are swallowed by the service).
        await _slotCacheService.SetAsync(specialtyIdStr, request.Date, result, CacheTtl, cancellationToken);

        return new SlotAvailabilityResponseDto(request.Date, request.SpecialtyId, result);
    }
}

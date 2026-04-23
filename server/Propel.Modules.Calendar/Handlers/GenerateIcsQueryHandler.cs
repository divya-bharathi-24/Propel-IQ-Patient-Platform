using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Interfaces;
using Propel.Modules.Calendar.Queries;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="GenerateIcsQuery"/> for <c>GET /api/calendar/ics?appointmentId={id}</c>
/// (us_035, us_036, AC-3, FR-036).
/// Validates appointment ownership, generates an RFC 5545-compliant ICS file via
/// <see cref="IIcsGeneratorService"/>, and returns the UTF-8–encoded bytes.
/// </summary>
public sealed class GenerateIcsQueryHandler : IRequestHandler<GenerateIcsQuery, byte[]>
{
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly IIcsGeneratorService _icsGeneratorService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<GenerateIcsQueryHandler> _logger;

    public GenerateIcsQueryHandler(
        IAppointmentBookingRepository appointmentRepo,
        IIcsGeneratorService icsGeneratorService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<GenerateIcsQueryHandler> logger)
    {
        _appointmentRepo     = appointmentRepo;
        _icsGeneratorService = icsGeneratorService;
        _httpContextAccessor = httpContextAccessor;
        _logger              = logger;
    }

    public async Task<byte[]> Handle(GenerateIcsQuery request, CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT claims (OWASP A01)
        var patientIdStr = _httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException(
                "Patient identity could not be resolved from JWT.");

        if (!Guid.TryParse(patientIdStr, out var patientId))
            throw new UnauthorizedAccessException("Invalid patientId claim in JWT.");

        // Load appointment with patient and specialty (OWASP A01 — validate ownership)
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(
            request.AppointmentId, cancellationToken);

        if (appointment is null || appointment.PatientId != patientId)
            throw new UnauthorizedAccessException(
                $"Appointment {request.AppointmentId} does not belong to patient {patientId}.");

        var icsContent = _icsGeneratorService.Generate(appointment);

        _logger.LogDebug(
            "ICS generated for AppointmentId={AppointmentId} PatientId={PatientId}",
            request.AppointmentId, patientId);

        return Encoding.UTF8.GetBytes(icsContent);
    }
}

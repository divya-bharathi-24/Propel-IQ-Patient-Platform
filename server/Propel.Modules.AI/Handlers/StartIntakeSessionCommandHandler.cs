using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Commands;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Services;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="StartIntakeSessionCommand"/> for <c>POST /api/intake/ai/session</c>
/// (US_028, AC-1).
/// <list type="number">
///   <item><b>Ownership check</b>: verifies <c>appointment.PatientId == command.PatientId</c>
///         so a patient cannot piggyback on another patient's appointment (OWASP A01).</item>
///   <item><b>Session creation</b>: creates an <see cref="Models.IntakeSession"/> in
///         <see cref="IntakeSessionStore"/> and returns the new <c>sessionId</c>.</item>
/// </list>
/// </summary>
public sealed class StartIntakeSessionCommandHandler
    : IRequestHandler<StartIntakeSessionCommand, StartSessionResponseDto>
{
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly IntakeSessionStore _sessionStore;
    private readonly ILogger<StartIntakeSessionCommandHandler> _logger;

    public StartIntakeSessionCommandHandler(
        IAppointmentBookingRepository appointmentRepo,
        IntakeSessionStore sessionStore,
        ILogger<StartIntakeSessionCommandHandler> logger)
    {
        _appointmentRepo = appointmentRepo;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<StartSessionResponseDto> Handle(
        StartIntakeSessionCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load appointment and verify it exists.
        var appointment = await _appointmentRepo.GetByIdWithRelatedAsync(
            request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Appointment '{request.AppointmentId}' was not found.");

        // Step 2 — Ownership check: appointment must belong to the requesting patient (OWASP A01).
        if (appointment.PatientId != request.PatientId)
        {
            _logger.LogWarning(
                "StartIntakeSession_Forbidden: PatientId={PatientId} attempted to start session " +
                "for AppointmentId={AppointmentId} owned by PatientId={OwnerPatientId}",
                request.PatientId, request.AppointmentId, appointment.PatientId);

            throw new AiForbiddenAccessException(
                $"Appointment '{request.AppointmentId}' does not belong to the requesting patient.");
        }

        // Step 3 — Create session.
        var sessionId = _sessionStore.CreateSession(request.PatientId, request.AppointmentId);

        _logger.LogInformation(
            "AI intake session {SessionId} started for PatientId={PatientId} AppointmentId={AppointmentId}",
            sessionId, request.PatientId, request.AppointmentId);

        return new StartSessionResponseDto(sessionId);
    }
}

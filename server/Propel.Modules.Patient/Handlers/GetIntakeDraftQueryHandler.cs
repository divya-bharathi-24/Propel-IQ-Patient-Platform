using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="GetIntakeDraftQuery"/> for <c>GET /api/intake/{appointmentId}/draft</c>
/// (US_017, AC-3, AC-4).
/// <list type="number">
///   <item>Loads the <see cref="Domain.Entities.IntakeRecord"/> patient-scoped by
///         <c>(appointmentId, patientId)</c> (OWASP A01 — Broken Access Control).</item>
///   <item>Returns <see cref="KeyNotFoundException"/> (→ HTTP 404) when no record exists
///         or when <c>draftData</c> is null — there is no draft to restore (AC-4).</item>
///   <item>Returns <see cref="GetIntakeDraftResult"/> with <see cref="IntakeDraftDto"/>
///         containing the partial draft JSON and <c>lastModifiedAt</c>.</item>
/// </list>
/// No audit log entry is written for draft reads — drafts are never considered final PHI
/// (only completed intake records trigger PHI-access audit, per HIPAA minimum-necessary).
/// </summary>
public sealed class GetIntakeDraftQueryHandler : IRequestHandler<GetIntakeDraftQuery, GetIntakeDraftResult>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly ILogger<GetIntakeDraftQueryHandler> _logger;

    public GetIntakeDraftQueryHandler(
        IIntakeRepository intakeRepo,
        ILogger<GetIntakeDraftQueryHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _logger = logger;
    }

    public async Task<GetIntakeDraftResult> Handle(
        GetIntakeDraftQuery request,
        CancellationToken cancellationToken)
    {
        var record = await _intakeRepo.GetByAppointmentIdAsync(
            request.AppointmentId,
            request.PatientId,
            cancellationToken);

        if (record is null || record.DraftData is null)
        {
            _logger.LogDebug(
                "No draft found for AppointmentId {AppointmentId} PatientId {PatientId}",
                request.AppointmentId, request.PatientId);
            throw new KeyNotFoundException(
                $"No draft exists for appointment {request.AppointmentId}.");
        }

        _logger.LogDebug(
            "Draft retrieved for AppointmentId {AppointmentId}", request.AppointmentId);

        return new GetIntakeDraftResult(
            new IntakeDraftDto(record.DraftData, record.LastModifiedAt));
    }
}

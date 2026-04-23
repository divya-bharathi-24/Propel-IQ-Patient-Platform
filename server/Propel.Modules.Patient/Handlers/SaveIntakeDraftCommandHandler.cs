using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="SaveIntakeDraftCommand"/> for
/// <c>POST /api/intake/{appointmentId}/draft</c> (US_017, AC-3, AC-4).
/// <list type="number">
///   <item>Upserts only the <c>draftData</c> JSONB column and <c>lastModifiedAt</c> on the
///         existing <see cref="Domain.Entities.IntakeRecord"/> via
///         <see cref="IIntakeRepository.UpsertDraftAsync"/>.</item>
///   <item>Does NOT modify <c>completedAt</c>, <c>source</c>, or any primary JSONB intake
///         column (Demographics, MedicalHistory, Symptoms, Medications).</item>
/// </list>
/// No audit log entry is written for draft saves — drafts are not considered completed PHI
/// records (only the full PUT write triggers the IntakeUpdate audit entry).
/// </summary>
public sealed class SaveIntakeDraftCommandHandler : IRequestHandler<SaveIntakeDraftCommand, Unit>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly ILogger<SaveIntakeDraftCommandHandler> _logger;

    public SaveIntakeDraftCommandHandler(
        IIntakeRepository intakeRepo,
        ILogger<SaveIntakeDraftCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _logger = logger;
    }

    public async Task<Unit> Handle(SaveIntakeDraftCommand request, CancellationToken cancellationToken)
    {
        await _intakeRepo.UpsertDraftAsync(
            request.AppointmentId,
            request.PatientId,
            request.DraftData,
            cancellationToken);

        _logger.LogInformation(
            "Draft saved for AppointmentId {AppointmentId} PatientId {PatientId} CorrelationId {CorrelationId}",
            request.AppointmentId, request.PatientId, request.CorrelationId);

        return Unit.Value;
    }
}

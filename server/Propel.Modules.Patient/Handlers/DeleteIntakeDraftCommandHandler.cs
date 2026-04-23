using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="DeleteIntakeDraftCommand"/> for
/// <c>DELETE /api/intake/draft?appointmentId={id}</c> (US_029, AC-3 edge case — "Start Fresh").
/// <list type="number">
///   <item>Loads the incomplete manual draft patient-scoped by
///         <c>(appointmentId, patientId, source=Manual, completedAt=null)</c> (OWASP A01).</item>
///   <item>Removes the record if found; responds with 204 No Content regardless (idempotent).</item>
/// </list>
/// No audit log entry is written — draft deletion is a non-clinical UX action (task spec).
/// </summary>
public sealed class DeleteIntakeDraftCommandHandler : IRequestHandler<DeleteIntakeDraftCommand, Unit>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly ILogger<DeleteIntakeDraftCommandHandler> _logger;

    public DeleteIntakeDraftCommandHandler(
        IIntakeRepository intakeRepo,
        ILogger<DeleteIntakeDraftCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _logger = logger;
    }

    public async Task<Unit> Handle(DeleteIntakeDraftCommand command, CancellationToken cancellationToken)
    {
        var draft = await _intakeRepo.GetManualDraftAsync(
            command.AppointmentId, command.PatientId, cancellationToken);

        if (draft is null)
        {
            // Idempotent — no draft found, nothing to delete (AC-3 edge case)
            _logger.LogInformation(
                "DeleteIntakeDraft: no manual draft found for " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                command.AppointmentId, command.PatientId);
            return Unit.Value;
        }

        await _intakeRepo.RemoveAsync(draft, cancellationToken);

        _logger.LogInformation(
            "DeleteIntakeDraft: removed IntakeRecordId={IntakeRecordId} " +
            "AppointmentId={AppointmentId} PatientId={PatientId}",
            draft.Id, command.AppointmentId, command.PatientId);

        return Unit.Value;
    }
}

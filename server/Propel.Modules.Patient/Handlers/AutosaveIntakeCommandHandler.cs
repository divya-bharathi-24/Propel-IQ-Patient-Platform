using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="AutosaveIntakeCommand"/> for <c>POST /api/intake/autosave</c>
/// (US_029, AC-3 edge case — autosave draft).
/// <list type="number">
///   <item><b>Load</b>: fetch the latest incomplete manual draft patient-scoped by
///         <c>(appointmentId, patientId, source=Manual, completedAt=null)</c> (OWASP A01).</item>
///   <item><b>UPSERT</b>: INSERT a new <see cref="IntakeRecord"/> when no draft exists;
///         UPDATE the four JSONB columns on the existing draft otherwise.
///         Never modifies <c>completedAt</c>.</item>
/// </list>
/// No audit log entry is written for draft autosaves to avoid audit log spam (task spec).
/// </summary>
public sealed class AutosaveIntakeCommandHandler : IRequestHandler<AutosaveIntakeCommand, Unit>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly ILogger<AutosaveIntakeCommandHandler> _logger;

    public AutosaveIntakeCommandHandler(
        IIntakeRepository intakeRepo,
        ILogger<AutosaveIntakeCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _logger = logger;
    }

    public async Task<Unit> Handle(AutosaveIntakeCommand command, CancellationToken cancellationToken)
    {
        var existing = await _intakeRepo.GetManualDraftAsync(
            command.AppointmentId, command.PatientId, cancellationToken);

        if (existing is null)
        {
            var draft = new IntakeRecord
            {
                Id = Guid.NewGuid(),
                PatientId = command.PatientId,
                AppointmentId = command.AppointmentId,
                Source = IntakeSource.Manual,
                Demographics = command.Demographics ?? JsonDocument.Parse("{}"),
                MedicalHistory = command.MedicalHistory ?? JsonDocument.Parse("{}"),
                Symptoms = command.Symptoms ?? JsonDocument.Parse("{}"),
                Medications = command.Medications ?? JsonDocument.Parse("{}"),
                CompletedAt = null,
                LastModifiedAt = DateTime.UtcNow
            };
            await _intakeRepo.UpsertAsync(draft, cancellationToken);

            _logger.LogInformation(
                "AutosaveIntake: INSERT new draft IntakeRecordId={IntakeRecordId} " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                draft.Id, command.AppointmentId, command.PatientId);
        }
        else
        {
            existing.Demographics = command.Demographics ?? existing.Demographics;
            existing.MedicalHistory = command.MedicalHistory ?? existing.MedicalHistory;
            existing.Symptoms = command.Symptoms ?? existing.Symptoms;
            existing.Medications = command.Medications ?? existing.Medications;
            existing.LastModifiedAt = DateTime.UtcNow;
            await _intakeRepo.UpsertAsync(existing, cancellationToken);

            _logger.LogInformation(
                "AutosaveIntake: UPDATE draft IntakeRecordId={IntakeRecordId} " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                existing.Id, command.AppointmentId, command.PatientId);
        }

        return Unit.Value;
    }
}

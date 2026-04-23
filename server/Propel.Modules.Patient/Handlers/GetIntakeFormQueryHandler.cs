using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="GetIntakeFormQuery"/> for <c>GET /api/intake/form?appointmentId={id}</c>
/// (US_029, AC-3 edge case — resume draft).
/// <list type="number">
///   <item>Loads the latest incomplete manual draft
///         (<c>source = Manual, completedAt IS NULL</c>) patient-scoped by
///         <c>(appointmentId, patientId)</c> (OWASP A01 — AD-2 CQRS read model).</item>
///   <item>Loads the completed AI-extracted record
///         (<c>source = AI, completedAt IS NOT NULL</c>) for the same appointment.</item>
///   <item>Returns <see cref="IntakeFormResponseDto"/> with both; either may be <c>null</c>
///         when no matching record exists (e.g. new patient starting fresh).</item>
/// </list>
/// No audit log entry is written for this read — draft retrieval is a lightweight UX action.
/// </summary>
public sealed class GetIntakeFormQueryHandler : IRequestHandler<GetIntakeFormQuery, IntakeFormResponseDto>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly ILogger<GetIntakeFormQueryHandler> _logger;

    public GetIntakeFormQueryHandler(
        IIntakeRepository intakeRepo,
        ILogger<GetIntakeFormQueryHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _logger = logger;
    }

    public async Task<IntakeFormResponseDto> Handle(
        GetIntakeFormQuery request,
        CancellationToken cancellationToken)
    {
        // Load both records in parallel — independent queries (AD-2 CQRS read model, AsNoTracking in repo)
        var manualDraftTask = _intakeRepo.GetManualDraftAsync(
            request.AppointmentId, request.PatientId, cancellationToken);
        var aiExtractedTask = _intakeRepo.GetAiExtractedAsync(
            request.AppointmentId, request.PatientId, cancellationToken);

        await Task.WhenAll(manualDraftTask, aiExtractedTask);

        var manualDraft = manualDraftTask.Result;
        var aiExtracted = aiExtractedTask.Result;

        _logger.LogInformation(
            "GetIntakeForm: AppointmentId={AppointmentId} PatientId={PatientId} " +
            "HasManualDraft={HasManualDraft} HasAiExtracted={HasAiExtracted}",
            request.AppointmentId, request.PatientId,
            manualDraft is not null, aiExtracted is not null);

        return new IntakeFormResponseDto(
            request.AppointmentId,
            ManualDraft: manualDraft is not null ? ToDto(manualDraft) : null,
            AiExtracted: aiExtracted is not null ? ToDto(aiExtracted) : null);
    }

    private static IntakeDraftDataDto ToDto(IntakeRecord record) => new(
        record.Demographics,
        record.MedicalHistory,
        record.Symptoms,
        record.Medications);
}

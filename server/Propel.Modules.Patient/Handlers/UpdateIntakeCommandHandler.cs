using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Audit;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Exceptions;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="UpdateIntakeCommand"/> for <c>PUT /api/intake/{appointmentId}</c>
/// (US_017, AC-2, AC-3).
/// <list type="number">
///   <item><b>Load</b>: fetch existing <see cref="IntakeRecord"/> patient-scoped by
///         <c>(appointmentId, patientId)</c> (OWASP A01).</item>
///   <item><b>Concurrency check</b>: compare <c>RowVersion</c> from the <c>If-Match</c>
///         header against the server-side xmin. Throws
///         <see cref="IntakeConcurrencyConflictException"/> (→ 409) on mismatch (AC-2).</item>
///   <item><b>Validation</b>: manually invoke <see cref="IValidator{T}"/> for required
///         demographic fields. On failure: persist partial data to <c>draftData</c> (AC-3),
///         throw <see cref="IntakeMissingFieldsException"/> (→ 422).</item>
///   <item><b>UPSERT</b>: UPDATE existing record (set all JSONB columns, <c>completedAt</c>,
///         <c>lastModifiedAt</c>, clear <c>draftData</c>) or INSERT a new record when none
///         existed. Never creates a duplicate row (DR-004, FR-010, FR-019).</item>
///   <item><b>Audit</b>: write immutable <see cref="AuditLog"/> entry with updated field names
///         but no PHI values (HIPAA minimum-necessary, AC-2).</item>
/// </list>
/// </summary>
public sealed class UpdateIntakeCommandHandler
    : IRequestHandler<UpdateIntakeCommand, UpdateIntakeResult>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IValidator<UpdateIntakeCommand> _validator;
    private readonly ILogger<UpdateIntakeCommandHandler> _logger;

    public UpdateIntakeCommandHandler(
        IIntakeRepository intakeRepo,
        IAuditLogRepository auditLogRepo,
        IValidator<UpdateIntakeCommand> validator,
        ILogger<UpdateIntakeCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _auditLogRepo = auditLogRepo;
        _validator = validator;
        _logger = logger;
    }

    public async Task<UpdateIntakeResult> Handle(
        UpdateIntakeCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1: Load patient-scoped record (OWASP A01 — cross-patient access prevention)
        var existing = await _intakeRepo.GetByAppointmentIdAsync(
            request.AppointmentId,
            request.PatientId,
            cancellationToken);

        // Step 2: Optimistic concurrency check (AC-2 — concurrent staff/patient edit)
        if (existing is not null && !string.IsNullOrEmpty(request.RowVersion))
        {
            var serverETag = Convert.ToBase64String(BitConverter.GetBytes(existing.RowVersion));
            if (serverETag != request.RowVersion)
            {
                _logger.LogWarning(
                    "Concurrency conflict on IntakeRecord for AppointmentId {AppointmentId} PatientId {PatientId}",
                    request.AppointmentId, request.PatientId);

                var conflictETag = serverETag;
                var conflictDto = GetIntakeQueryHandler.MapToDto(existing, conflictETag);
                throw new IntakeConcurrencyConflictException(conflictDto);
            }
        }

        // Step 3: FluentValidation of required demographic fields (called manually — AFTER
        // concurrency check, and with custom 422 + draft-save path on failure, AC-3)
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var missingFields = validationResult.Errors
                .Select(e => e.PropertyName)
                .Distinct()
                .ToArray();

            _logger.LogInformation(
                "Intake validation failed for AppointmentId {AppointmentId}: {MissingFields}",
                request.AppointmentId, string.Join(", ", missingFields));

            // Persist partial form data as draft so the patient can resume without losing input (AC-3)
            if (request.Demographics is not null
                || request.MedicalHistory is not null
                || request.Symptoms is not null
                || request.Medications is not null)
            {
                var partialDraftDoc = BuildPartialDraftDocument(request);
                await _intakeRepo.UpsertDraftAsync(
                    request.AppointmentId,
                    request.PatientId,
                    partialDraftDoc,
                    cancellationToken);
            }

            throw new IntakeMissingFieldsException(missingFields);
        }

        // Step 4: UPSERT — update existing tracked entity or create a new record (DR-004, FR-010)
        bool isNew = existing is null;
        var now = DateTime.UtcNow;

        if (isNew)
        {
            existing = new IntakeRecord
            {
                Id = Guid.NewGuid(),
                PatientId = request.PatientId,
                AppointmentId = request.AppointmentId,
                Source = IntakeSource.Manual,
                Demographics = request.Demographics!,
                MedicalHistory = request.MedicalHistory ?? JsonDocument.Parse("{}"),
                Symptoms = request.Symptoms ?? JsonDocument.Parse("{}"),
                Medications = request.Medications ?? JsonDocument.Parse("{}"),
                CompletedAt = now,
                LastModifiedAt = now,
                DraftData = null
            };
        }
        else
        {
            // Entity is already tracked by EF Core — mutate in place
            existing!.Demographics = request.Demographics ?? existing.Demographics;
            existing.MedicalHistory = request.MedicalHistory ?? existing.MedicalHistory;
            existing.Symptoms = request.Symptoms ?? existing.Symptoms;
            existing.Medications = request.Medications ?? existing.Medications;
            existing.CompletedAt = now;
            existing.LastModifiedAt = now;
            existing.DraftData = null; // clear draft on full save (AC-2)
        }

        var saved = await _intakeRepo.UpsertAsync(existing, cancellationToken);

        var refreshedETag = Convert.ToBase64String(BitConverter.GetBytes(saved.RowVersion));
        var dto = GetIntakeQueryHandler.MapToDto(saved, refreshedETag);

        // Step 5: Audit log — field names only, no PHI values (HIPAA minimum-necessary, AC-2)
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.PatientId,
            PatientId = request.PatientId,
            Role = "Patient",
            Action = IntakeAuditActions.IntakeUpdate,
            EntityType = nameof(IntakeRecord),
            EntityId = saved.Id,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                fieldsUpdated = new[] { "demographics", "medicalHistory", "symptoms", "medications" },
                isNew
            })),
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = now
        }, cancellationToken);

        _logger.LogInformation(
            "IntakeRecord {IntakeId} {Operation} for AppointmentId {AppointmentId} PatientId {PatientId}",
            saved.Id, isNew ? "created" : "updated", request.AppointmentId, request.PatientId);

        return new UpdateIntakeResult(dto, refreshedETag);
    }

    // Merges non-null form sections into a single JSON object for the draft autosave path.
    private static JsonDocument BuildPartialDraftDocument(UpdateIntakeCommand request)
    {
        var dict = new Dictionary<string, JsonElement>();

        if (request.Demographics is not null)
            dict["demographics"] = request.Demographics.RootElement.Clone();

        if (request.MedicalHistory is not null)
            dict["medicalHistory"] = request.MedicalHistory.RootElement.Clone();

        if (request.Symptoms is not null)
            dict["symptoms"] = request.Symptoms.RootElement.Clone();

        if (request.Medications is not null)
            dict["medications"] = request.Medications.RootElement.Clone();

        return JsonDocument.Parse(JsonSerializer.Serialize(dict));
    }
}

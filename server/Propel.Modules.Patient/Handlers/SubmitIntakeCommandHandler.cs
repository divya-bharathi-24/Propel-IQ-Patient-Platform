using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Audit;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Exceptions;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="SubmitIntakeCommand"/> for <c>POST /api/intake/submit</c>
/// (US_029, AC-3, AC-4, FR-057).
/// <list type="number">
///   <item><b>Validate</b>: required fields in Demographics (fullName, dateOfBirth, phone) and
///         at least one Symptoms entry. On failure → throws
///         <see cref="IntakeMissingFieldsException"/> (→ HTTP 422 via GlobalExceptionFilter).</item>
///   <item><b>UPSERT</b>: load the existing manual draft; INSERT if none exists or UPDATE JSONB
///         columns; always sets <c>completedAt = UtcNow</c>.</item>
///   <item><b>Audit</b>: write immutable <see cref="AuditLog"/> entry <c>IntakeCompleted</c>
///         (FR-057). No PHI values in audit details (HIPAA minimum-necessary).</item>
/// </list>
/// </summary>
public sealed class SubmitIntakeCommandHandler : IRequestHandler<SubmitIntakeCommand, Unit>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<SubmitIntakeCommandHandler> _logger;

    public SubmitIntakeCommandHandler(
        IIntakeRepository intakeRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<SubmitIntakeCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<Unit> Handle(SubmitIntakeCommand command, CancellationToken cancellationToken)
    {
        // Step 1 — Semantic validation of required intake fields (→ 422 on failure, AC-4)
        ValidateRequiredFields(command);

        // Step 2 — UPSERT: same pattern as autosave, but sets completedAt = UtcNow (AC-3)
        var existing = await _intakeRepo.GetManualDraftAsync(
            command.AppointmentId, command.PatientId, cancellationToken);

        Guid recordId;
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            var record = new IntakeRecord
            {
                Id = Guid.NewGuid(),
                PatientId = command.PatientId,
                AppointmentId = command.AppointmentId,
                Source = IntakeSource.Manual,
                Demographics = command.Demographics ?? JsonDocument.Parse("{}"),
                MedicalHistory = command.MedicalHistory ?? JsonDocument.Parse("{}"),
                Symptoms = command.Symptoms ?? JsonDocument.Parse("{}"),
                Medications = command.Medications ?? JsonDocument.Parse("{}"),
                CompletedAt = now,
                LastModifiedAt = now
            };
            await _intakeRepo.UpsertAsync(record, cancellationToken);
            recordId = record.Id;

            _logger.LogInformation(
                "SubmitIntake: INSERT completed IntakeRecordId={IntakeRecordId} " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                recordId, command.AppointmentId, command.PatientId);
        }
        else
        {
            existing.Demographics = command.Demographics ?? existing.Demographics;
            existing.MedicalHistory = command.MedicalHistory ?? existing.MedicalHistory;
            existing.Symptoms = command.Symptoms ?? existing.Symptoms;
            existing.Medications = command.Medications ?? existing.Medications;
            existing.CompletedAt = now;
            existing.LastModifiedAt = now;
            await _intakeRepo.UpsertAsync(existing, cancellationToken);
            recordId = existing.Id;

            _logger.LogInformation(
                "SubmitIntake: UPDATE draft → completed IntakeRecordId={IntakeRecordId} " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                recordId, command.AppointmentId, command.PatientId);
        }

        // Step 3 — Audit log (FR-057, AD-7, HIPAA minimum-necessary — no PHI in Details)
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.PatientId,
            PatientId = command.PatientId,
            Role = "Patient",
            Action = IntakeAuditActions.IntakeCompleted,
            EntityType = nameof(IntakeRecord),
            EntityId = recordId,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                appointmentId = command.AppointmentId,
                source = nameof(IntakeSource.Manual)
            })),
            Timestamp = now
        }, cancellationToken);

        return Unit.Value;
    }

    /// <summary>
    /// Validates required fields inside the JSONB payloads.
    /// Throws <see cref="IntakeMissingFieldsException"/> (→ HTTP 422) when any required
    /// field is absent or empty (AC-4, US_029).
    /// </summary>
    private static void ValidateRequiredFields(SubmitIntakeCommand command)
    {
        var missingFields = new List<string>();

        if (command.Demographics is null)
        {
            missingFields.Add("demographics");
        }
        else
        {
            if (!HasNonEmptyStringProperty(command.Demographics, "fullName"))
                missingFields.Add("demographics.fullName");

            if (!HasNonEmptyStringProperty(command.Demographics, "dateOfBirth"))
                missingFields.Add("demographics.dateOfBirth");
            else if (!IsValidDate(GetStringProperty(command.Demographics, "dateOfBirth")))
                missingFields.Add("demographics.dateOfBirth");

            if (!HasNonEmptyStringProperty(command.Demographics, "phone"))
                missingFields.Add("demographics.phone");
            else if (!IsValidPhone(GetStringProperty(command.Demographics, "phone")))
                missingFields.Add("demographics.phone");
        }

        if (command.Symptoms is null || !HasNonEmptyArray(command.Symptoms, "symptoms"))
            missingFields.Add("symptoms.symptoms");

        if (missingFields.Count > 0)
            throw new IntakeMissingFieldsException(missingFields);
    }

    private static bool HasNonEmptyStringProperty(JsonDocument doc, string propertyName)
    {
        if (!doc.RootElement.TryGetProperty(propertyName, out var prop))
            return false;
        return prop.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(prop.GetString());
    }

    private static string? GetStringProperty(JsonDocument doc, string propertyName)
    {
        if (!doc.RootElement.TryGetProperty(propertyName, out var prop))
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static bool HasNonEmptyArray(JsonDocument doc, string propertyName)
    {
        if (!doc.RootElement.TryGetProperty(propertyName, out var prop))
            return false;
        return prop.ValueKind == JsonValueKind.Array && prop.GetArrayLength() > 0;
    }

    private static bool IsValidDate(string? value)
        => !string.IsNullOrWhiteSpace(value) && DateOnly.TryParse(value, out _);

    private static bool IsValidPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Accepts international format: optional leading +, digits/spaces/hyphens, 7–15 chars
        return System.Text.RegularExpressions.Regex.IsMatch(value, @"^\+?[\d\s\-]{7,15}$");
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Commands;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Models;
using Propel.Modules.AI.Services;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="SubmitAiIntakeCommand"/> for <c>POST /api/intake/ai/submit</c>
/// (US_028, AC-4).
/// <list type="number">
///   <item><b>Load session</b>: retrieve from <see cref="IntakeSessionStore"/>; validate ownership (OWASP A01).</item>
///   <item><b>Validate fields</b>: at least one field must be present; otherwise bad request.</item>
///   <item><b>Duplicate check</b>: verify no <c>IntakeRecord</c> exists for this
///         <c>(patientId, appointmentId)</c> pair — returns HTTP 409 via
///         <see cref="AiIntakeDuplicateException"/> if found (AC-4).</item>
///   <item><b>Map fields</b>: group <c>ExtractedField</c> values by JSONB column prefix
///         (<c>demographics.*</c>, <c>medicalHistory.*</c>, <c>symptoms.*</c>, <c>medications.*</c>).</item>
///   <item><b>INSERT</b>: persist new <c>IntakeRecord</c> via <see cref="IIntakeRepository.UpsertAsync"/>
///         with <c>Source = AI</c> and <c>CompletedAt = UtcNow</c>.</item>
///   <item><b>Audit</b>: write immutable <see cref="AuditLog"/> entry for <c>"AiIntakeSubmitted"</c> (AD-7).</item>
///   <item><b>Cleanup</b>: remove session from <see cref="IntakeSessionStore"/>.</item>
/// </list>
/// </summary>
public sealed class SubmitAiIntakeCommandHandler
    : IRequestHandler<SubmitAiIntakeCommand, SubmitAiIntakeResult>
{
    private readonly IntakeSessionStore _sessionStore;
    private readonly IIntakeRepository _intakeRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<SubmitAiIntakeCommandHandler> _logger;

    // Field name prefix convention: "{group}.{fieldName}" maps to the four JSONB columns.
    private const string DemographicsPrefix = "demographics.";
    private const string MedicalHistoryPrefix = "medicalHistory.";
    private const string SymptomsPrefix = "symptoms.";
    private const string MedicationsPrefix = "medications.";

    public SubmitAiIntakeCommandHandler(
        IntakeSessionStore sessionStore,
        IIntakeRepository intakeRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<SubmitAiIntakeCommandHandler> logger)
    {
        _sessionStore = sessionStore;
        _intakeRepo = intakeRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<SubmitAiIntakeResult> Handle(
        SubmitAiIntakeCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load session and validate ownership (OWASP A01).
        var session = _sessionStore.GetSession(request.SessionId)
            ?? throw new KeyNotFoundException(
                $"AI intake session '{request.SessionId}' was not found or has expired.");

        if (session.PatientId != request.PatientId)
        {
            _logger.LogWarning(
                "SubmitAiIntake_Forbidden: PatientId={PatientId} attempted to submit " +
                "session {SessionId} owned by PatientId={OwnerPatientId}",
                request.PatientId, request.SessionId, session.PatientId);

            throw new AiForbiddenAccessException(
                $"AI intake session '{request.SessionId}' does not belong to the requesting patient.");
        }

        // Step 2 — Validate at least one extracted field exists.
        IReadOnlyList<ExtractedField> fields;
        lock (session.ExtractedFields)
        {
            fields = [.. session.ExtractedFields];
        }

        if (fields.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot submit an AI intake with no extracted fields. Complete at least one intake turn first.");
        }

        // Step 3 — Duplicate check: prevent double-submission for same appointment (AC-4).
        var existing = await _intakeRepo.GetByAppointmentIdAsync(
            session.AppointmentId, session.PatientId, cancellationToken);

        if (existing is not null)
        {
            _logger.LogWarning(
                "SubmitAiIntake_Duplicate: IntakeRecord already exists for " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                session.AppointmentId, session.PatientId);

            throw new AiIntakeDuplicateException(session.AppointmentId);
        }

        // Step 4 — Map extracted fields into the four JSONB column groups.
        var demographics = BuildJsonGroup(fields, DemographicsPrefix);
        var medicalHistory = BuildJsonGroup(fields, MedicalHistoryPrefix);
        var symptoms = BuildJsonGroup(fields, SymptomsPrefix);
        var medications = BuildJsonGroup(fields, MedicationsPrefix);

        // Step 5 — INSERT IntakeRecord with Source = AI.
        var intakeRecord = new IntakeRecord
        {
            Id = Guid.NewGuid(),
            PatientId = session.PatientId,
            AppointmentId = session.AppointmentId,
            Source = IntakeSource.AI,
            Demographics = demographics,
            MedicalHistory = medicalHistory,
            Symptoms = symptoms,
            Medications = medications,
            CompletedAt = DateTime.UtcNow,
            LastModifiedAt = DateTime.UtcNow
        };

        var saved = await _intakeRepo.UpsertAsync(intakeRecord, cancellationToken);

        _logger.LogInformation(
            "AI intake submitted: IntakeRecordId={IntakeRecordId} PatientId={PatientId} AppointmentId={AppointmentId}",
            saved.Id, session.PatientId, session.AppointmentId);

        // Step 6 — Write audit log entry (AD-7, HIPAA minimum-necessary — no PHI in Details).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = session.PatientId,
            PatientId = session.PatientId,
            Role = "Patient",
            Action = "AiIntakeSubmitted",
            EntityType = nameof(IntakeRecord),
            EntityId = saved.Id,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                sessionId = request.SessionId,
                appointmentId = session.AppointmentId,
                fieldCount = fields.Count,
                source = nameof(IntakeSource.AI)
            })),
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        // Step 7 — Remove session from store (cleanup).
        _sessionStore.Remove(request.SessionId);

        return new SubmitAiIntakeResult(saved.Id);
    }

    /// <summary>
    /// Builds a <see cref="JsonDocument"/> containing all fields whose <c>FieldName</c>
    /// starts with <paramref name="prefix"/>. The prefix is stripped from each key so only
    /// the short field name appears in the JSONB payload.
    /// Returns an empty JSON object (<c>{}</c>) when no fields match the prefix.
    /// </summary>
    private static JsonDocument BuildJsonGroup(IReadOnlyList<ExtractedField> fields, string prefix)
    {
        var group = fields
            .Where(f => f.FieldName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                f => f.FieldName[prefix.Length..],
                f => f.Value,
                StringComparer.OrdinalIgnoreCase);

        return JsonDocument.Parse(JsonSerializer.Serialize(group));
    }
}

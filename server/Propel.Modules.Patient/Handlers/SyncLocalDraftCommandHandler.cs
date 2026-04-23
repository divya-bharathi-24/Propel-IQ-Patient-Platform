using System.IO;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Audit;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="SyncLocalDraftCommand"/> for
/// <c>POST /api/intake/sync-local-draft</c> (US_030, AC-3).
/// <list type="number">
///   <item><b>Ownership + load</b>: loads the manual draft patient-scoped by
///         <c>(appointmentId, patientId)</c> (OWASP A01). If no draft exists, the local data
///         is treated as strictly newer and applied immediately.</item>
///   <item><b>Conflict check</b>: compares <c>LocalTimestamp</c> against
///         <c>IntakeRecord.lastModifiedAt</c>. Local strictly newer → apply; otherwise →
///         <c>Applied = false</c> with both server copies returned for client reconciliation.</item>
///   <item><b>UPSERT</b>: when applied, serialises the <see cref="IntakeFieldMap"/> into a
///         <see cref="JsonDocument"/> and persists it to <c>draftData</c> via
///         <see cref="IIntakeRepository.UpsertDraftAsync"/>.</item>
///   <item><b>Audit</b>: writes an immutable <see cref="AuditLog"/> entry on BOTH paths
///         (<c>EventType = "LocalDraftSync"</c>) so sync operations are traceable.</item>
/// </list>
/// </summary>
public sealed class SyncLocalDraftCommandHandler : IRequestHandler<SyncLocalDraftCommand, SyncLocalDraftResult>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<SyncLocalDraftCommandHandler> _logger;

    public SyncLocalDraftCommandHandler(
        IIntakeRepository intakeRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<SyncLocalDraftCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<SyncLocalDraftResult> Handle(
        SyncLocalDraftCommand command,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load existing draft (patient-scoped for OWASP A01 compliance)
        var existing = await _intakeRepo.GetManualDraftAsync(
            command.AppointmentId, command.PatientId, cancellationToken);

        var now = DateTime.UtcNow;
        bool applied;
        IntakeFieldMap? serverFields = null;
        DateTimeOffset? serverLastModifiedAt = null;

        if (existing is null)
        {
            // No existing draft — local is implicitly the newest version; apply unconditionally.
            var draftDoc = SerializeToJsonDocument(command.LocalFields);
            await _intakeRepo.UpsertDraftAsync(
                command.AppointmentId, command.PatientId, draftDoc, cancellationToken);

            applied = true;

            _logger.LogInformation(
                "SyncLocalDraft: Applied (no server draft) AppointmentId={AppointmentId} PatientId={PatientId}",
                command.AppointmentId, command.PatientId);
        }
        else
        {
            // Step 2 — Conflict check: compare local timestamp vs server lastModifiedAt
            // Treat null lastModifiedAt as epoch (effectively always older than any local timestamp).
            var serverModifiedAt = existing.LastModifiedAt.HasValue
                ? new DateTimeOffset(existing.LastModifiedAt.Value, TimeSpan.Zero)
                : DateTimeOffset.MinValue;

            if (command.LocalTimestamp > serverModifiedAt)
            {
                // Local is strictly newer — apply the local draft.
                var draftDoc = SerializeToJsonDocument(command.LocalFields);
                await _intakeRepo.UpsertDraftAsync(
                    command.AppointmentId, command.PatientId, draftDoc, cancellationToken);

                applied = true;

                _logger.LogInformation(
                    "SyncLocalDraft: Applied (local newer) AppointmentId={AppointmentId} PatientId={PatientId} " +
                    "LocalTimestamp={LocalTimestamp} ServerModifiedAt={ServerModifiedAt}",
                    command.AppointmentId, command.PatientId,
                    command.LocalTimestamp, serverModifiedAt);
            }
            else
            {
                // Server is equal-or-newer — conflict; return server version for client reconciliation.
                applied = false;
                serverLastModifiedAt = serverModifiedAt;
                serverFields = DeserializeFromJsonDocument(existing.DraftData);

                _logger.LogInformation(
                    "SyncLocalDraft: Conflict (server equal-or-newer) AppointmentId={AppointmentId} " +
                    "PatientId={PatientId} LocalTimestamp={LocalTimestamp} ServerModifiedAt={ServerModifiedAt}",
                    command.AppointmentId, command.PatientId,
                    command.LocalTimestamp, serverModifiedAt);
            }
        }

        // Step 3 — Audit log on BOTH paths (AIR-S03; no PHI in Details).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.PatientId,
            PatientId = command.PatientId,
            Role = "Patient",
            Action = IntakeAuditActions.LocalDraftSync,
            EntityType = nameof(IntakeRecord),
            EntityId = command.AppointmentId,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                appointmentId = command.AppointmentId,
                applied,
                localTimestamp = command.LocalTimestamp
            })),
            Timestamp = now
        }, cancellationToken);

        return new SyncLocalDraftResult(applied, serverFields, serverLastModifiedAt);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Serialises an <see cref="IntakeFieldMap"/> into a single <see cref="JsonDocument"/>
    /// with the four section keys (<c>demographics</c>, <c>medicalHistory</c>,
    /// <c>symptoms</c>, <c>medications</c>) suitable for storage in the <c>draftData</c> column.
    /// </summary>
    private static JsonDocument SerializeToJsonDocument(IntakeFieldMap fields)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();

        if (fields.Demographics is not null)
        {
            writer.WritePropertyName("demographics");
            fields.Demographics.WriteTo(writer);
        }

        if (fields.MedicalHistory is not null)
        {
            writer.WritePropertyName("medicalHistory");
            fields.MedicalHistory.WriteTo(writer);
        }

        if (fields.Symptoms is not null)
        {
            writer.WritePropertyName("symptoms");
            fields.Symptoms.WriteTo(writer);
        }

        if (fields.Medications is not null)
        {
            writer.WritePropertyName("medications");
            fields.Medications.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        ms.Seek(0, SeekOrigin.Begin);
        return JsonDocument.Parse(ms);
    }

    /// <summary>
    /// Deserialises a <c>draftData</c> <see cref="JsonDocument"/> back into an
    /// <see cref="IntakeFieldMap"/> for the conflict response payload.
    /// Returns <c>null</c> when the document is <c>null</c> or structurally invalid.
    /// </summary>
    private static IntakeFieldMap? DeserializeFromJsonDocument(JsonDocument? doc)
    {
        if (doc is null) return null;

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        JsonDocument? demographics = TryParseSection(root, "demographics");
        JsonDocument? medicalHistory = TryParseSection(root, "medicalHistory");
        JsonDocument? symptoms = TryParseSection(root, "symptoms");
        JsonDocument? medications = TryParseSection(root, "medications");

        return new IntakeFieldMap(demographics, medicalHistory, symptoms, medications);
    }

    private static JsonDocument? TryParseSection(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var section)) return null;
        if (section.ValueKind == JsonValueKind.Null) return null;

        try
        {
            return JsonDocument.Parse(section.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

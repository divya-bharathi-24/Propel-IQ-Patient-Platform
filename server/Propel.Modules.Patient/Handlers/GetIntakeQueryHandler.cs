using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Audit;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="GetIntakeQuery"/> for <c>GET /api/intake/{appointmentId}</c>
/// (US_017, AC-1).
/// <list type="number">
///   <item>Loads the <see cref="IntakeRecord"/> patient-scoped by <c>(appointmentId, patientId)</c>
///         to prevent cross-patient data leakage (OWASP A01 — Broken Access Control).</item>
///   <item>Throws <see cref="KeyNotFoundException"/> (→ HTTP 404) when no record exists.</item>
///   <item>Returns <see cref="GetIntakeResult"/> containing the DTO and the
///         Base64-encoded <c>xmin</c> row version as an ETag.</item>
///   <item>Writes an immutable PHI-access <see cref="AuditLog"/> entry (HIPAA §164.312(b)).</item>
/// </list>
/// </summary>
public sealed class GetIntakeQueryHandler : IRequestHandler<GetIntakeQuery, GetIntakeResult>
{
    private readonly IIntakeRepository _intakeRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<GetIntakeQueryHandler> _logger;

    public GetIntakeQueryHandler(
        IIntakeRepository intakeRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<GetIntakeQueryHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<GetIntakeResult> Handle(GetIntakeQuery request, CancellationToken cancellationToken)
    {
        var record = await _intakeRepo.GetByAppointmentIdAsync(
            request.AppointmentId,
            request.PatientId,
            cancellationToken);

        if (record is null)
        {
            _logger.LogWarning(
                "IntakeRecord not found for AppointmentId {AppointmentId} PatientId {PatientId}",
                request.AppointmentId, request.PatientId);
            throw new KeyNotFoundException(
                $"Intake record for appointment {request.AppointmentId} not found.");
        }

        var etag = Convert.ToBase64String(BitConverter.GetBytes(record.RowVersion));
        var dto = MapToDto(record, etag);

        // PHI-access audit entry — no PHI values in details (HIPAA §164.312(b), AC-1)
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.PatientId,
            PatientId = request.PatientId,
            Role = "Patient",
            Action = IntakeAuditActions.IntakeRead,
            EntityType = nameof(IntakeRecord),
            EntityId = record.Id,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "IntakeRecord {IntakeId} read for AppointmentId {AppointmentId}",
            record.Id, request.AppointmentId);

        return new GetIntakeResult(dto, etag);
    }

    internal static IntakeRecordDto MapToDto(IntakeRecord record, string etag) => new(
        record.Id,
        record.PatientId,
        record.AppointmentId,
        record.Source.ToString(),
        record.Demographics,
        record.MedicalHistory,
        record.Symptoms,
        record.Medications,
        record.CompletedAt,
        record.LastModifiedAt,
        etag);
}

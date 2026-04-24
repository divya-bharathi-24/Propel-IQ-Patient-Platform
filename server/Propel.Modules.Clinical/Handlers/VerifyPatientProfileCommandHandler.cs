using System.Text.Json;
using MediatR;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Clinical.Commands;
using Propel.Modules.Clinical.Exceptions;

namespace Propel.Modules.Clinical.Handlers;

/// <summary>
/// Handles <see cref="VerifyPatientProfileCommand"/> for
/// <c>POST /api/staff/patients/{patientId}/360-view/verify</c> (AC-3, AC-4).
///
/// <list type="number">
///   <item>Checks for unresolved Critical <see cref="DataConflict"/> records; throws <see cref="UnresolvedConflictsException"/> (→ HTTP 409) if any exist (AC-4).</item>
///   <item>Upserts a <see cref="PatientProfileVerification"/> record with <c>Status = Verified</c>, staff ID, and UTC timestamp (AC-3).</item>
///   <item>Appends an immutable <see cref="AuditLog"/> entry (AC-3, NFR-013).</item>
/// </list>
///
/// Staff <c>userId</c> is read from <see cref="VerifyPatientProfileCommand.StaffUserId"/> which is
/// sourced exclusively from the verified JWT claim in the controller (OWASP A01).
/// </summary>
public sealed class VerifyPatientProfileCommandHandler : IRequestHandler<VerifyPatientProfileCommand>
{
    private readonly IDataConflictRepository _conflictRepo;
    private readonly IPatientProfileVerificationRepository _verificationRepo;
    private readonly IAuditLogRepository _auditRepo;

    public VerifyPatientProfileCommandHandler(
        IDataConflictRepository conflictRepo,
        IPatientProfileVerificationRepository verificationRepo,
        IAuditLogRepository auditRepo)
    {
        _conflictRepo = conflictRepo;
        _verificationRepo = verificationRepo;
        _auditRepo = auditRepo;
    }

    public async Task Handle(VerifyPatientProfileCommand command, CancellationToken cancellationToken)
    {
        // ── 1. Conflict gate: block if any Critical + Unresolved conflicts exist (AC-4) ──
        var conflicts = await _conflictRepo.GetUnresolvedCriticalConflictsAsync(
            command.PatientId, cancellationToken);

        if (conflicts.Count > 0)
            throw new UnresolvedConflictsException(conflicts);

        // ── 2. Upsert PatientProfileVerification (AC-3) ──────────────────────────
        await _verificationRepo.UpsertAsync(new PatientProfileVerification
        {
            Id = Guid.NewGuid(),
            PatientId = command.PatientId,
            Status = VerificationStatus.Verified,
            VerifiedBy = command.StaffUserId,
            VerifiedAt = DateTime.UtcNow
        }, cancellationToken);

        // ── 3. Append immutable audit log (AC-3, NFR-013, AD-7) ─────────────────
        await _auditRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.StaffUserId,
            PatientId = command.PatientId,
            Role = "Staff",
            Action = "Update",
            EntityType = "PatientProfileVerification",
            EntityId = command.PatientId,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                Status = "Verified",
                VerifiedBy = command.StaffUserId,
                VerifiedAt = DateTime.UtcNow
            })),
            Timestamp = DateTime.UtcNow
        }, cancellationToken);
    }
}

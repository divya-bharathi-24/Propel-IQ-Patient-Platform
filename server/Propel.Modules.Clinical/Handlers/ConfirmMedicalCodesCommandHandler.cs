using System.Text.Json;
using MediatR;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Handlers;

/// <summary>
/// Handles <see cref="ConfirmMedicalCodesCommand"/> for
/// <c>POST /api/medical-codes/confirm</c> (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
///
/// <list type="number">
///   <item>Accepts: sets <c>VerificationStatus = Accepted</c>, <c>VerifiedBy</c>, <c>VerifiedAt</c> on each targeted <c>MedicalCode</c> row.</item>
///   <item>Rejects: sets <c>VerificationStatus = Rejected</c> and stores <c>RejectionReason</c>.</item>
///   <item>Manual entries: inserts a new <c>MedicalCode</c> row with <c>IsManualEntry = true</c>, <c>VerificationStatus = Accepted</c>, and no source document FK.</item>
///   <item>Writes one immutable <see cref="AuditLog"/> entry per individual code decision (FR-058, NFR-013, AD-7).</item>
///   <item>Returns a <see cref="ConfirmCodesResponse"/> including the count of codes still in <c>Pending</c> state after the submission.</item>
/// </list>
///
/// Codes not referenced in any list retain <c>VerificationStatus = Pending</c> (partial submission allowed).
/// Multi-reviewer scenario: the most recent submission overwrites <c>VerifiedBy</c> and <c>VerifiedAt</c>;
/// every prior review action is preserved immutably in <see cref="AuditLog"/> (FR-058).
///
/// All LINQ queries use parameterised EF Core — no raw SQL (OWASP A03).
/// <c>StaffUserId</c> is sourced exclusively from <see cref="ConfirmMedicalCodesCommand.StaffUserId"/>,
/// which is set from the validated JWT claim in the controller (OWASP A01).
/// </summary>
public sealed class ConfirmMedicalCodesCommandHandler
    : IRequestHandler<ConfirmMedicalCodesCommand, ConfirmCodesResponse>
{
    private readonly IMedicalCodeRepository    _codeRepo;
    private readonly IAuditLogRepository        _auditRepo;
    private readonly IAiAgreementEventEmitter   _agreementEmitter;

    public ConfirmMedicalCodesCommandHandler(
        IMedicalCodeRepository codeRepo,
        IAuditLogRepository auditRepo,
        IAiAgreementEventEmitter agreementEmitter)
    {
        _codeRepo          = codeRepo;
        _auditRepo         = auditRepo;
        _agreementEmitter  = agreementEmitter;
    }

    public async Task<ConfirmCodesResponse> Handle(
        ConfirmMedicalCodesCommand command,
        CancellationToken cancellationToken)
    {
        var now      = DateTime.UtcNow;
        var auditBag = new List<AuditLog>();

        // ── 1. Process accepted codes ─────────────────────────────────────────
        if (command.Accepted.Count > 0)
        {
            var acceptedIds = (IReadOnlySet<Guid>)command.Accepted.ToHashSet();

            var acceptedCodes = await _codeRepo.GetByIdsAndPatientAsync(
                command.PatientId, acceptedIds, cancellationToken);

            foreach (var code in acceptedCodes)
            {
                code.VerificationStatus = MedicalCodeVerificationStatus.Accepted;
                code.VerifiedBy         = command.StaffUserId;
                code.VerifiedAt         = now;
                code.RejectionReason    = null;

                auditBag.Add(BuildAuditEntry(
                    command.StaffUserId, command.PatientId, code.Id,
                    "Accepted", null, now));

                // AIR-Q01: emit agreement metric event (AC-1, us_048).
                await _agreementEmitter.EmitAgreementEventAsync(
                    code.Id, code.Code, MedicalCodeVerificationStatus.Accepted, cancellationToken);
            }
        }

        // ── 2. Process rejected codes ─────────────────────────────────────────
        if (command.Rejected.Count > 0)
        {
            var rejectedIds = command.Rejected.ToDictionary(r => r.Id, r => r.RejectionReason);
            var rejectedSet = (IReadOnlySet<Guid>)rejectedIds.Keys.ToHashSet();

            var rejectedCodes = await _codeRepo.GetByIdsAndPatientAsync(
                command.PatientId, rejectedSet, cancellationToken);

            foreach (var code in rejectedCodes)
            {
                var reason = rejectedIds.TryGetValue(code.Id, out var r) ? r : string.Empty;

                code.VerificationStatus = MedicalCodeVerificationStatus.Rejected;
                code.VerifiedBy         = command.StaffUserId;
                code.VerifiedAt         = now;
                code.RejectionReason    = reason;

                auditBag.Add(BuildAuditEntry(
                    command.StaffUserId, command.PatientId, code.Id,
                    "Rejected", reason, now));

                // AIR-Q01: emit agreement metric event (AC-1, us_048).
                await _agreementEmitter.EmitAgreementEventAsync(
                    code.Id, code.Code, MedicalCodeVerificationStatus.Rejected, cancellationToken);
            }
        }

        // ── 3. Persist accepted + rejected mutations ──────────────────────────
        await _codeRepo.SaveAsync(cancellationToken);

        // ── 4. Process manual entries ─────────────────────────────────────────
        foreach (var entry in command.Manual)
        {
            var newCode = new MedicalCode
            {
                Id                 = Guid.NewGuid(),
                PatientId          = command.PatientId,
                Code               = entry.Code,
                CodeType           = entry.CodeType,
                Description        = entry.Description,
                Confidence         = 1m,      // Staff-confirmed; treat as full confidence
                SourceDocumentId   = null,    // No AI source document for manual entries
                IsManualEntry      = true,
                VerificationStatus = MedicalCodeVerificationStatus.Accepted,
                VerifiedBy         = command.StaffUserId,
                VerifiedAt         = now
            };

            await _codeRepo.AddAsync(newCode, cancellationToken);

            auditBag.Add(BuildAuditEntry(
                command.StaffUserId, command.PatientId, newCode.Id,
                "ManualEntry", null, now));
        }

        if (command.Manual.Count > 0)
            await _codeRepo.SaveAsync(cancellationToken);

        // ── 5. Write audit entries (each in an isolated scope via IAuditLogRepository) ─
        foreach (var entry in auditBag)
            await _auditRepo.AppendAsync(entry, cancellationToken);

        // ── 6. Count remaining Pending codes for this patient ─────────────────
        int pendingCount = await _codeRepo.CountPendingAsync(command.PatientId, cancellationToken);

        return new ConfirmCodesResponse(
            AcceptedCount: command.Accepted.Count,
            RejectedCount: command.Rejected.Count,
            ManualCount:   command.Manual.Count,
            PendingCount:  pendingCount);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds an immutable <see cref="AuditLog"/> for a single code decision (FR-058, NFR-013, AD-7).
    /// </summary>
    private static AuditLog BuildAuditEntry(
        Guid staffUserId,
        Guid patientId,
        Guid codeId,
        string actionDetail,
        string? rejectionReason,
        DateTime timestamp)
    {
        var details = new Dictionary<string, object?>
        {
            ["actionType"]       = "MedicalCodeDecision",
            ["status"]           = actionDetail,
            ["affectedRecordId"] = codeId,
        };

        if (rejectionReason is not null)
            details["rejectionReason"] = rejectionReason;

        return new AuditLog
        {
            Id         = Guid.NewGuid(),
            UserId     = staffUserId,
            PatientId  = patientId,
            Role       = "Staff",
            Action     = "Update",
            EntityType = "MedicalCode",
            EntityId   = codeId,
            Details    = JsonDocument.Parse(JsonSerializer.Serialize(details)),
            Timestamp  = timestamp
        };
    }
}

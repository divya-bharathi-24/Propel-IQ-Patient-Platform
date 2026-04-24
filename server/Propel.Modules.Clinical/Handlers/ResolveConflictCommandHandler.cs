using System.Text.Json;
using MediatR;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Handlers;

/// <summary>
/// Handles <see cref="ResolveConflictCommand"/> for
/// <c>POST /api/conflicts/{id}/resolve</c> (AC-3, EP-008-II/us_044, task_003).
///
/// <list type="number">
///   <item>Fetch <see cref="DataConflict"/> by ID; throw <see cref="KeyNotFoundException"/> (→ HTTP 404) if not found.</item>
///   <item>Capture before-state for the audit log (FR-057).</item>
///   <item>Apply resolution: <c>ResolutionStatus = Resolved</c>, <c>ResolvedValue</c>,
///         <c>ResolvedBy</c> (from JWT), <c>ResolvedAt = UTC</c> (AC-3).
///         Upsert behaviour — re-resolving an already-Resolved conflict overwrites fields (AC-3 edge case).</item>
///   <item>Persist via <c>IDataConflictRepository.UpdateAsync</c>.</item>
///   <item>Append an immutable <see cref="AuditLog"/> entry with before/after state (FR-057, FR-058, AD-7).</item>
///   <item>Return <see cref="DataConflictDto"/> of the updated record.</item>
/// </list>
///
/// Staff <c>userId</c> is read from <see cref="ResolveConflictCommand.StaffUserId"/> which is
/// sourced exclusively from the verified JWT claim in the controller (OWASP A01).
/// </summary>
public sealed class ResolveConflictCommandHandler : IRequestHandler<ResolveConflictCommand, DataConflictDto>
{
    private readonly IDataConflictRepository _conflictRepo;
    private readonly IAuditLogRepository _auditRepo;

    public ResolveConflictCommandHandler(
        IDataConflictRepository conflictRepo,
        IAuditLogRepository auditRepo)
    {
        _conflictRepo = conflictRepo;
        _auditRepo    = auditRepo;
    }

    public async Task<DataConflictDto> Handle(
        ResolveConflictCommand command,
        CancellationToken cancellationToken)
    {
        // ── 1. Fetch conflict (tracked) — 404 if not found ──────────────────────
        var conflict = await _conflictRepo.GetByIdAsync(command.ConflictId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"DataConflict '{command.ConflictId}' was not found.");

        // ── 2. Capture before-state for audit log (FR-057) ──────────────────────
        var beforeStatus      = conflict.ResolutionStatus.ToString();
        var beforeResolvedVal = conflict.ResolvedValue;
        var beforeResolvedBy  = conflict.ResolvedBy;

        // ── 3. Apply resolution (upsert — AC-3 edge case) ───────────────────────
        conflict.ResolutionStatus = DataConflictResolutionStatus.Resolved;
        conflict.ResolvedValue    = command.ResolvedValue;
        conflict.ResolvedBy       = command.StaffUserId;
        conflict.ResolvedAt       = DateTimeOffset.UtcNow;

        await _conflictRepo.UpdateAsync(conflict, cancellationToken);

        // ── 4. Append immutable audit log (FR-057, FR-058, AD-7) ────────────────
        var auditDetails = new
        {
            Before = new
            {
                ResolutionStatus = beforeStatus,
                ResolvedValue    = beforeResolvedVal,
                ResolvedBy       = beforeResolvedBy
            },
            After = new
            {
                ResolutionStatus = conflict.ResolutionStatus.ToString(),
                conflict.ResolvedValue,
                ResolvedBy     = command.StaffUserId,
                ResolvedAt     = conflict.ResolvedAt,
                ResolutionNote = command.ResolutionNote
            }
        };

        await _auditRepo.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            UserId     = command.StaffUserId,
            PatientId  = conflict.PatientId,
            Role       = "Staff",
            Action     = "ConflictResolved",
            EntityType = "DataConflict",
            EntityId   = conflict.Id,
            Details    = JsonDocument.Parse(JsonSerializer.Serialize(auditDetails)),
            Timestamp  = DateTime.UtcNow
        }, cancellationToken);

        // ── 5. Return updated DTO ────────────────────────────────────────────────
        return GetPatientConflictsQueryHandler.MapToDto(conflict);
    }
}

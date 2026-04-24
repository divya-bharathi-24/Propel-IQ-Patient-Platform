using MediatR;
using Propel.Domain.Dtos;

namespace Propel.Modules.Clinical.Commands;

/// <summary>
/// MediatR command: resolves a <c>DataConflict</c> record by setting
/// <c>ResolutionStatus = Resolved</c>, <c>ResolvedValue</c>, <c>ResolvedBy</c>, and
/// <c>ResolvedAt = UTC</c>, then writes an immutable <c>AuditLog</c> entry (FR-058).
/// (EP-008-II/us_044, task_003, AC-3)
/// <para>
/// Sent by <c>ConflictsController.ResolveConflict</c> in response to
/// <c>POST /api/conflicts/{id}/resolve</c> (RBAC: Staff — NFR-006).
/// </para>
/// <para>
/// <see cref="StaffUserId"/> is sourced exclusively from the verified JWT claim in the controller —
/// never from the request body (OWASP A01: Broken Access Control).
/// </para>
/// <para>
/// Upsert behaviour (edge case): resolving an already-Resolved conflict overwrites
/// <c>ResolvedValue</c>, <c>ResolvedBy</c>, and <c>ResolvedAt</c>; a new <c>AuditLog</c> entry
/// is appended preserving the full resolution history (FR-057).
/// </para>
/// </summary>
public sealed record ResolveConflictCommand(
    /// <summary>Primary key of the <c>DataConflict</c> record to resolve.</summary>
    Guid ConflictId,

    /// <summary>
    /// Authenticated Staff member performing the resolution. Sourced from JWT — never from the
    /// request body (OWASP A01).
    /// </summary>
    Guid StaffUserId,

    /// <summary>The authoritative value chosen for the conflicting clinical field.</summary>
    string ResolvedValue,

    /// <summary>
    /// Optional free-text justification. Stored in <c>AuditLog.Details</c> only — not on the entity.
    /// </summary>
    string? ResolutionNote) : IRequest<DataConflictDto>;

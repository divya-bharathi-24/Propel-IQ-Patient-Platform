using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// INSERT-only repository for <see cref="AuditLog"/> records.
/// No UPDATE or DELETE operations are permitted at the repository level (AD-7, NFR-013).
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Appends a new immutable audit log entry. The implementation must never
    /// issue UPDATE or DELETE SQL against the audit_logs table.
    /// </summary>
    Task AppendAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
}

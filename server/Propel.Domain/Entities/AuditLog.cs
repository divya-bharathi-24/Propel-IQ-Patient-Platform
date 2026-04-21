using System.Text.Json;

namespace Propel.Domain.Entities;

/// <summary>
/// Immutable audit log entity capturing all state-changing operations in the system.
/// Per AD-7, all properties use <c>init</c> accessor to enforce immutability at the C# level.
/// No navigation properties are defined to enforce the write-only repository pattern and
/// prevent accidental lazy-load or cascade scenarios.
/// A PostgreSQL trigger (configured in task_003) rejects any UPDATE or DELETE against the
/// <c>audit_logs</c> table at the database level (AC-1).
/// All mapping is deferred to EF fluent configuration in <c>PropelIQ.Infrastructure</c>.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; init; }

    /// <summary>The authenticated user who performed the action. Nullable for anonymous events such as FailedLogin with unknown email or RateLimitBlock. Raw FK — no navigation property (AD-7).</summary>
    public Guid? UserId { get; init; }

    /// <summary>The patient affected by the action, if applicable. Raw FK — no navigation property (AD-7).</summary>
    public Guid? PatientId { get; init; }

    /// <summary>The role of the authenticated user at the time of the event (e.g. "Patient", "Admin"). Nullable for anonymous events (AC-1, US_013).</summary>
    public string? Role { get; init; }

    /// <summary>The type of operation performed (e.g. Create, Update, Delete).</summary>
    public required string Action { get; init; }

    /// <summary>The CLR or table name of the entity that was modified.</summary>
    public required string EntityType { get; init; }

    /// <summary>The primary key of the affected entity.</summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// Arbitrary JSON payload capturing before/after state or contextual metadata.
    /// Mapped to a PostgreSQL JSONB column in EF fluent configuration.
    /// </summary>
    public JsonDocument? Details { get; init; }

    /// <summary>IP address of the client that originated the request.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Distributed trace / correlation identifier for cross-service observability.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>UTC timestamp when the audit event occurred.</summary>
    public DateTime Timestamp { get; init; }
}

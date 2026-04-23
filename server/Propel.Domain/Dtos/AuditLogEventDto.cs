using System.Text.Json;

namespace Propel.Domain.Dtos;

/// <summary>
/// API DTO representing a single audit log event (US_047, AC-1, FR-057, FR-058).
/// Lives in <c>Propel.Domain</c> so both the read repository interface and module
/// handlers can reference it without circular dependency.
/// <para>
/// <see cref="Details"/> is nullable; it is populated only for clinical modification events
/// that carry before/after state (FR-058). For all other events it is null.
/// </para>
/// </summary>
public sealed record AuditLogEventDto(
    Guid Id,
    Guid? UserId,
    string? UserRole,
    string EntityType,
    string EntityId,
    string ActionType,
    string? IpAddress,
    DateTime Timestamp,
    JsonDocument? Details
);

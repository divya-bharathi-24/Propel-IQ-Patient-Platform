namespace Propel.Domain.Entities;

/// <summary>
/// Key-value store for system-wide configurable settings (US_033, FR-032).
/// Provides runtime reconfiguration of reminder intervals without redeployment.
/// All mapping is deferred to EF fluent configuration in <c>PropelIQ.Infrastructure</c>.
/// </summary>
public sealed class SystemSetting
{
    /// <summary>
    /// Unique setting key (primary key). Max 100 characters.
    /// Example: <c>"reminder_interval_hours"</c> — value is a JSON array of interval hours.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Setting value stored as TEXT. May be a JSON-serialised array or a plain scalar.
    /// Example: <c>"[48,24,2]"</c> for reminder intervals.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>UTC timestamp of the most recent write to this setting.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Optional FK to the <see cref="User"/> who last modified this setting.
    /// Null for system-seeded defaults or automated writes.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }
}

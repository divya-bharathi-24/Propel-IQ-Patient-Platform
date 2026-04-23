namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for reading and writing configurable system settings from the
/// <c>system_settings</c> table (US_033, FR-032, AC-3).
/// Implementations are consumed by <c>ReminderSchedulerService</c> and
/// <c>UpdateReminderIntervalsCommandHandler</c> to support runtime reconfiguration of
/// reminder intervals without redeployment.
/// </summary>
public interface ISystemSettingsRepository
{
    /// <summary>
    /// Returns the configured reminder interval values in hours (e.g., [48, 24, 2]).
    /// Reads the <c>"reminder_interval_hours"</c> key from <c>system_settings</c>
    /// and deserialises the JSON array value.
    /// Falls back to <c>[48, 24, 2]</c> defaults if the key is absent.
    /// </summary>
    Task<int[]> GetReminderIntervalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists new reminder interval values to the <c>"reminder_interval_hours"</c> key
    /// in <c>system_settings</c> as a JSON array (AC-3 — future reminders recalculated).
    /// Upserts the record: inserts if absent, updates if present.
    /// </summary>
    Task SetReminderIntervalsAsync(int[] intervalHours, Guid updatedByUserId, CancellationToken cancellationToken = default);
}

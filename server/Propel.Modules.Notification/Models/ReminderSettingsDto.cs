namespace Propel.Modules.Notification.Models;

/// <summary>
/// Response DTO for <c>GET /api/settings/reminders</c> and the result of
/// <c>UpdateReminderIntervalsCommand</c> (US_033, AC-3).
/// </summary>
public sealed record ReminderSettingsDto(int[] IntervalHours);

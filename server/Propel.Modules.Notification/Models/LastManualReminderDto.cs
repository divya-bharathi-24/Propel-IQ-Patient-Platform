namespace Propel.Modules.Notification.Models;

/// <summary>
/// Projection of the last manually-triggered reminder sent for an appointment (US_034, AC-3).
/// Embedded in <see cref="AppointmentDetailDto"/> for confirmation display in the staff UI.
/// </summary>
public sealed record LastManualReminderDto(
    /// <summary>UTC timestamp of the most recent successful manual reminder dispatch.</summary>
    DateTimeOffset SentAt,
    /// <summary>Display name of the staff member who triggered the reminder.</summary>
    string TriggeredByStaffName);

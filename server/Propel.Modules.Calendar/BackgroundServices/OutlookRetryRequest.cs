namespace Propel.Modules.Calendar.BackgroundServices;

/// <summary>
/// Payload enqueued to <see cref="System.Threading.Channels.Channel{T}"/> when a Microsoft Graph
/// API call fails during Outlook Calendar sync (us_036, AC-4).
/// The retry background service waits 10 minutes before re-attempting the Graph API call.
/// </summary>
public sealed record OutlookRetryRequest(
    Guid AppointmentId,
    Guid PatientId,
    string AccessToken,
    DateTime FailedAt);

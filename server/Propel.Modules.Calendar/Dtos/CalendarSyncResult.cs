namespace Propel.Modules.Calendar.Dtos;

/// <summary>
/// Discriminated result of a booking-time calendar sync attempt (US_052, AC-4, NFR-018).
/// <para>
/// <list type="bullet">
///   <item><see cref="Synced"/> — external event created successfully; booking confirmation proceeds.</item>
///   <item><see cref="Failed"/> — external API unavailable or token missing; <c>CalendarSync.syncStatus = Failed</c>
///         is persisted and ICS download fallback is offered to the user (NFR-018).</item>
/// </list>
/// </para>
/// Callers must never throw — all exceptional paths must resolve to <see cref="Failed"/>.
/// </summary>
public abstract record CalendarSyncResult
{
    private CalendarSyncResult() { }

    /// <summary>External calendar event created; <paramref name="ExternalEventId"/> is the provider-assigned ID.</summary>
    public sealed record Synced(string ExternalEventId) : CalendarSyncResult;

    /// <summary>
    /// External API call failed or provider is not connected.
    /// <paramref name="Reason"/> is a human-readable message safe for staff display (no PII).
    /// </summary>
    public sealed record Failed(string Reason) : CalendarSyncResult;
}

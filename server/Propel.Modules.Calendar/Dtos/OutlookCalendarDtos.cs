using Propel.Domain.Enums;

namespace Propel.Modules.Calendar.Dtos;

/// <summary>
/// Result DTO returned by <c>POST /api/calendar/outlook/initiate</c> (us_036, AC-1).
/// Contains the Microsoft OAuth 2.0 authorization URL for the FE to redirect to.
/// </summary>
public sealed record InitiateOutlookSyncResultDto(string AuthorizationUrl);

/// <summary>
/// Result DTO returned after a successful Outlook Calendar sync (us_036, AC-2).
/// Carries the sync outcome and the Outlook web event link for the FE.
/// </summary>
public sealed record CalendarSyncResultDto(
    CalendarSyncStatus SyncStatus,
    string? EventLink);

/// <summary>
/// Request model for <c>POST /api/calendar/outlook/initiate</c> (us_036, AC-1).
/// </summary>
public sealed record OutlookSyncRequest(Guid AppointmentId);

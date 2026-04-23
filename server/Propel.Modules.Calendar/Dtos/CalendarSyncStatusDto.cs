using Propel.Domain.Enums;

namespace Propel.Modules.Calendar.Dtos;

/// <summary>
/// Calendar sync status response DTO returned by <c>GET /api/calendar/google/status/{appointmentId}</c>.
/// </summary>
public sealed record CalendarSyncStatusDto(
    CalendarSyncStatus SyncStatus,
    string? EventLink,
    CalendarProvider Provider,
    DateTime? SyncedAt);

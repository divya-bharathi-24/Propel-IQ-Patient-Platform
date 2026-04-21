using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Calendar sync record entity tracking the synchronisation of an appointment to an
/// external calendar provider (Google, Apple, or Outlook) for a given patient (DR-017).
/// FK integrity to <see cref="Patient"/> and <see cref="Appointment"/> is enforced via
/// fluent configuration in <c>PropelIQ.Infrastructure</c> (AC-3).
/// </summary>
public sealed class CalendarSync
{
    public Guid Id { get; set; }

    /// <summary>The patient who owns the calendar entry.</summary>
    public Guid PatientId { get; set; }

    /// <summary>The appointment this sync record corresponds to.</summary>
    public Guid AppointmentId { get; set; }

    /// <summary>External calendar provider the event was synced to.</summary>
    public CalendarProvider Provider { get; set; }

    /// <summary>Event identifier assigned by the external calendar provider.</summary>
    public required string ExternalEventId { get; set; }

    /// <summary>Current synchronisation status.</summary>
    public CalendarSyncStatus SyncStatus { get; set; }

    /// <summary>UTC timestamp of the last successful sync, or null if never synced.</summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>Error detail from the most recent failed sync attempt, if applicable.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}

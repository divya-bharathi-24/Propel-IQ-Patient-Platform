using System.Text.Json;
using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Records patient intake data collected either via AI conversational interface or
/// manual form entry. The four JSONB properties hold structured JSON payloads;
/// column type is mapped to <c>jsonb</c> via HasColumnType in EF fluent config (task_002).
/// <para>
/// US_017 additions (task_002):
///   <c>DraftData</c> — partial JSONB snapshot persisted during autosave (AC-3, AC-4).
///   <c>LastModifiedAt</c> — UTC timestamp of the most recent write (draft or full save).
///   <c>RowVersion</c> — PostgreSQL <c>xmin</c> concurrency token for optimistic locking (AC-2).
/// </para>
/// </summary>
public sealed class IntakeRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid AppointmentId { get; set; }
    public IntakeSource Source { get; set; }

    // JSONB columns — mapped via HasColumnType("jsonb") in fluent config (task_002)
    public JsonDocument Demographics { get; set; } = null!;
    public JsonDocument MedicalHistory { get; set; } = null!;
    public JsonDocument Symptoms { get; set; } = null!;
    public JsonDocument Medications { get; set; } = null!;

    public DateTime? CompletedAt { get; set; }

    // US_017 — partial draft snapshot (AC-3, AC-4). Nullable jsonb column; null when no draft.
    public JsonDocument? DraftData { get; set; }

    // US_017 — UTC timestamp updated on every write (draft or full save).
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token mapped to PostgreSQL <c>xmin</c> system column (US_017, AC-2).
    /// Must be <c>uint</c> for Npgsql xmin concurrency support. No migration required — xmin
    /// exists on all PostgreSQL tables by default.
    /// </summary>
    public uint RowVersion { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}

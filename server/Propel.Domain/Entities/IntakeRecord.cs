using System.Text.Json;
using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Records patient intake data collected either via AI conversational interface or
/// manual form entry. The four JSONB properties hold structured JSON payloads;
/// column type is mapped to <c>jsonb</c> via HasColumnType in EF fluent config (task_002).
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

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}

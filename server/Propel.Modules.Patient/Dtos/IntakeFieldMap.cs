using System.Text.Json;

namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Structured snapshot of the four JSONB intake sections sent by the frontend
/// for AI session resume (US_030, AC-2) and local-draft sync (US_030, AC-3).
/// <para>
/// Each section is an optional raw JSON payload mirroring the intake form structure.
/// Null sections indicate the patient has not yet entered any data for that category.
/// </para>
/// </summary>
public sealed record IntakeFieldMap(
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications);

using System.Text.Json;

namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Read DTO returned by <c>GET /api/intake/{appointmentId}/draft</c> (US_017, AC-3, AC-4).
/// Contains only the partial draft snapshot and the timestamp of the most recent autosave.
/// </summary>
public sealed record IntakeDraftDto(
    JsonDocument DraftData,
    DateTime? LastModifiedAt);

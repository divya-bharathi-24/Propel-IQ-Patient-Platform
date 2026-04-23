namespace Propel.Domain.Entities;

/// <summary>
/// Value object representing a patient's communication preferences (US_015, AC-1).
/// Stored as plain JSONB in the <c>communication_preferences</c> column (non-PHI).
/// </summary>
public sealed record PatientCommunicationPreferences(
    bool? EmailEnabled,
    bool? SmsEnabled,
    bool? PushEnabled);

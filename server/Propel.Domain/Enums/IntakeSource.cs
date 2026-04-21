namespace Propel.Domain.Enums;

/// <summary>
/// Indicates whether a patient intake record was collected via AI conversational
/// interface or manual form entry.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum IntakeSource
{
    AI,
    Manual
}

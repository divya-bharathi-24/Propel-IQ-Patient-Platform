namespace Propel.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of a Patient account.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum PatientStatus
{
    Active,
    Deactivated
}

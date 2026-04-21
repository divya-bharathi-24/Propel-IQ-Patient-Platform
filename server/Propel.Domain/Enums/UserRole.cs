namespace Propel.Domain.Enums;

/// <summary>
/// Represents the access role of a User in the platform.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum UserRole
{
    Patient,
    Staff,
    Admin
}

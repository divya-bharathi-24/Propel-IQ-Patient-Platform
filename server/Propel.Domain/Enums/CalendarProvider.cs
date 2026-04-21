namespace Propel.Domain.Enums;

/// <summary>
/// External calendar provider for calendar sync operations.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum CalendarProvider
{
    Google,
    Apple,
    Outlook
}

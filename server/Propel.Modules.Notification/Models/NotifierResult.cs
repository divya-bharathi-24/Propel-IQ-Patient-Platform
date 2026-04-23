namespace Propel.Modules.Notification.Models;

/// <summary>
/// Result of a single notification dispatch attempt (email or SMS channel).
/// Returned by <see cref="Notifiers.IEmailNotifier"/> and <see cref="Notifiers.ISmsNotifier"/>
/// so callers can act on failure without catching exceptions (NFR-018).
/// </summary>
public sealed record NotifierResult(bool IsSuccess, string? ErrorMessage = null);

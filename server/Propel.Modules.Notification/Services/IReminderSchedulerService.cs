namespace Propel.Modules.Notification.Services;

/// <summary>
/// Marker interface for <c>ReminderSchedulerService</c> (US_033, task_001).
/// Extracted for testability — unit tests can substitute a no-op stub via this interface.
/// The concrete implementation is registered as an <c>IHostedService</c> via
/// <c>AddHostedService&lt;ReminderSchedulerService&gt;()</c> in <c>Program.cs</c>.
/// </summary>
public interface IReminderSchedulerService
{
    // No additional methods — the scheduler lifecycle is managed entirely by the
    // IHostedService / BackgroundService contract (StartAsync / StopAsync).
    // This interface exists solely to enable substitution in unit tests.
}

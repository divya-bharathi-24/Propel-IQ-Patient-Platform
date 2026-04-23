namespace Propel.Modules.Appointment.Configuration;

/// <summary>
/// App settings POCO for appointment slot grid generation (US_018, task_002).
/// Bound from the <c>"SlotConfiguration"</c> section in <c>appsettings.json</c> via
/// <c>services.Configure&lt;SlotConfiguration&gt;(config.GetSection("SlotConfiguration"))</c>
/// in <c>Program.cs</c>.
/// </summary>
public sealed class SlotConfiguration
{
    /// <summary>Duration of each time slot in minutes. Default: 30.</summary>
    public int SlotDurationMinutes { get; init; } = 30;

    /// <summary>
    /// Business hours start time in <c>HH:mm</c> format (24-hour). Default: "09:00".
    /// Parsed via <see cref="TimeOnly.Parse(string)"/> at handler invocation time.
    /// </summary>
    public string BusinessHoursStart { get; init; } = "09:00";

    /// <summary>
    /// Business hours end time in <c>HH:mm</c> format (24-hour). Default: "17:00".
    /// The grid generates slots up to but not beyond this boundary.
    /// Parsed via <see cref="TimeOnly.Parse(string)"/> at handler invocation time.
    /// </summary>
    public string BusinessHoursEnd { get; init; } = "17:00";
}

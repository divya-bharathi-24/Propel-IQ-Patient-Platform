using Propel.Domain.Entities;

namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Generates RFC 5545-compliant ICS calendar content from an <see cref="Appointment"/>
/// (us_035, us_036, AC-3, FR-036).
/// Shared between the Google Calendar (us_035) and Outlook Calendar (us_036) flows.
/// </summary>
public interface IIcsGeneratorService
{
    /// <summary>
    /// Produces a RFC 5545-compliant VCALENDAR string for the given <paramref name="appointment"/>.
    /// The caller is responsible for encoding to bytes and setting <c>Content-Type: text/calendar</c>.
    /// </summary>
    string Generate(Appointment appointment);
}

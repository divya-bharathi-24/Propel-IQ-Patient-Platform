using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Propel.Domain.Entities;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// RFC 5545-compliant ICS generator using Ical.Net 4.x (us_035, us_036, AC-3, FR-036).
/// Implements <see cref="IIcsGeneratorService"/>; shared between the Google and Outlook
/// calendar sync flows.
/// </summary>
public sealed class IcsGeneratorService : IIcsGeneratorService
{
    private const string ClinicName = "PropelIQ Clinic";

    /// <inheritdoc />
    public string Generate(Appointment appointment)
    {
        var specialtyName = appointment.Specialty?.Name ?? "Unknown Specialty";

        var startDate = appointment.Date;
        var startTime = appointment.TimeSlotStart ?? TimeOnly.MinValue;
        var endTime   = appointment.TimeSlotEnd   ?? startTime.AddHours(1);

        var dtStart = new CalDateTime(
            startDate.Year, startDate.Month, startDate.Day,
            startTime.Hour, startTime.Minute, startTime.Second);

        var dtEnd = new CalDateTime(
            startDate.Year, startDate.Month, startDate.Day,
            endTime.Hour, endTime.Minute, endTime.Second);

        var vEvent = new CalendarEvent
        {
            Uid         = $"{appointment.Id}@propeliq.health",
            Summary     = $"Appointment — {specialtyName}",
            Location    = ClinicName,
            Description = $"Provider: {specialtyName}\\nBooking Ref: {appointment.Id}\\nClinic: {ClinicName}",
            Status      = "CONFIRMED",
            DtStart     = dtStart,
            DtEnd       = dtEnd
        };

        var calendar = new Ical.Net.Calendar();
        calendar.Events.Add(vEvent);

        var serializer = new CalendarSerializer();
        return serializer.SerializeToString(calendar);
    }
}

using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Propel.Domain.Entities;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Generates RFC 5545 ICS file bytes from an <see cref="Appointment"/> using Ical.Net 4.x
/// (us_035, AC-4 ICS fallback, FR-036).
/// </summary>
public sealed class IcsGenerationService
{
    /// <summary>
    /// Builds an ICS calendar byte array for the given <paramref name="appointment"/>.
    /// Returns UTF-8–encoded bytes suitable for HTTP download with
    /// <c>Content-Type: text/calendar; charset=utf-8</c>.
    /// </summary>
    public byte[] GenerateIcs(Appointment appointment)
    {
        var specialtyName = appointment.Specialty?.Name ?? "Unknown Specialty";
        const string clinicName = "PropelIQ Clinic";

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
            Summary     = $"Appointment: General — {specialtyName}",
            Location    = clinicName,
            Description = $"Provider: {specialtyName}\nBooking Ref: {appointment.Id}\nClinic: {clinicName}",
            DtStart     = dtStart,
            DtEnd       = dtEnd,
            Uid         = appointment.Id.ToString()
        };

        var calendar = new Ical.Net.Calendar();
        calendar.Events.Add(vEvent);

        var serializer = new CalendarSerializer();
        var icsText = serializer.SerializeToString(calendar);
        return System.Text.Encoding.UTF8.GetBytes(icsText);
    }
}

namespace Propel.Modules.Notification.Models;

/// <summary>
/// Appointment detail response DTO for <c>GET /api/staff/appointments/{id}</c> (US_034, AC-3).
/// Includes the <see cref="LastManualReminder"/> projection populated by
/// <c>GetStaffAppointmentDetailQueryHandler</c> when at least one manual reminder has
/// been successfully sent.
/// </summary>
public sealed record AppointmentDetailDto(
    Guid AppointmentId,
    string PatientName,
    string SpecialtyName,
    DateOnly Date,
    TimeOnly? TimeSlotStart,
    TimeOnly? TimeSlotEnd,
    string Status,
    /// <summary>
    /// Most recent manual reminder for this appointment; <c>null</c> when no manual
    /// reminder has been triggered yet.
    /// </summary>
    LastManualReminderDto? LastManualReminder);

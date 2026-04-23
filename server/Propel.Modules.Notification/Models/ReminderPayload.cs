namespace Propel.Modules.Notification.Models;

/// <summary>
/// Immutable data-transfer object carrying the appointment reminder content used to compose
/// both the email body and the SMS message text (US_033, AC-2).
/// </summary>
/// <param name="PatientName">Display name of the patient, used in the email greeting.</param>
/// <param name="AppointmentDate">Calendar date of the scheduled appointment.</param>
/// <param name="AppointmentTimeSlot">Start time of the scheduled appointment slot.</param>
/// <param name="ProviderSpecialty">Specialty display name shown to the patient (e.g., "Cardiology").</param>
/// <param name="ReferenceNumber">Booking reference in the format "APT-XXXXXXXX".</param>
public sealed record ReminderPayload(
    string PatientName,
    DateOnly AppointmentDate,
    TimeOnly AppointmentTimeSlot,
    string ProviderSpecialty,
    string ReferenceNumber);

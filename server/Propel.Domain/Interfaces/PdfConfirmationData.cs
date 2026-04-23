namespace Propel.Domain.Interfaces;

/// <summary>
/// Carries the appointment details required to populate the QuestPDF confirmation document
/// (US_021, AC-1). All string fields are pre-formatted by the caller.
/// </summary>
/// <param name="ReferenceNumber">Unique booking reference number (monospace in header).</param>
/// <param name="PatientName">Full name of the patient, used in the document body.</param>
/// <param name="AppointmentDate">Calendar date of the appointment.</param>
/// <param name="TimeSlotStart">Start time of the booked appointment slot.</param>
/// <param name="TimeSlotEnd">End time of the booked appointment slot.</param>
/// <param name="ProviderSpecialty">Name of the provider specialty (e.g., "Cardiology").</param>
/// <param name="ClinicName">Display name of the clinic or location.</param>
public record PdfConfirmationData(
    string ReferenceNumber,
    string PatientName,
    DateOnly AppointmentDate,
    TimeOnly TimeSlotStart,
    TimeOnly TimeSlotEnd,
    string ProviderSpecialty,
    string ClinicName
);

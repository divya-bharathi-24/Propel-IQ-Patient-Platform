using MediatR;

namespace Propel.Modules.Appointment.Events;

/// <summary>
/// MediatR notification published by <see cref="Handlers.CreateBookingCommandHandler"/>
/// immediately after the <c>Appointment</c> record is committed to the database (US_021, AC-2).
/// Consumed by <c>BookingConfirmedEventHandler</c> in the infrastructure layer to orchestrate
/// PDF generation and SendGrid email delivery (AD-3 — event-driven async notification).
/// </summary>
/// <param name="AppointmentId">Primary key of the newly booked appointment.</param>
/// <param name="PatientId">Primary key of the patient who made the booking.</param>
/// <param name="PatientEmail">Registered email address of the patient — used as the SendGrid recipient.</param>
/// <param name="PatientName">Full name of the patient — used in the PDF confirmation body.</param>
/// <param name="SpecialtyName">Display name of the booked specialty (e.g., "Cardiology").</param>
/// <param name="ClinicName">Display name of the clinic or location for the confirmation document.</param>
/// <param name="AppointmentDate">Calendar date of the booked appointment.</param>
/// <param name="TimeSlotStart">Start time of the booked appointment slot.</param>
/// <param name="TimeSlotEnd">End time of the booked appointment slot.</param>
/// <param name="ReferenceNumber">Unique booking reference number (e.g., "APT-A1B2C3D4").</param>
public record BookingConfirmedEvent(
    Guid AppointmentId,
    Guid PatientId,
    string PatientEmail,
    string PatientName,
    string SpecialtyName,
    string ClinicName,
    DateOnly AppointmentDate,
    TimeOnly TimeSlotStart,
    TimeOnly TimeSlotEnd,
    string ReferenceNumber) : INotification;

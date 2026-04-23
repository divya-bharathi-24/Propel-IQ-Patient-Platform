namespace Propel.Modules.Admin.Dtos;

/// <summary>
/// Request body for <c>POST /api/staff/walkin</c> (US_026, AC-2, AC-3).
/// <para>
/// Required fields depend on <see cref="Mode"/>:
/// <list type="bullet">
///   <item><c>link</c>: <see cref="PatientId"/> must be supplied.</item>
///   <item><c>create</c>: <see cref="Name"/> and <see cref="Email"/> are required; <see cref="ContactNumber"/> is optional.</item>
///   <item><c>anonymous</c>: No patient fields are required.</item>
/// </list>
/// <see cref="SpecialtyId"/> and <see cref="Date"/> are always required.
/// </para>
/// </summary>
public sealed record WalkInBookingDto(
    /// <summary>Walk-in mode: "link", "create", or "anonymous" (case-insensitive).</summary>
    string Mode,

    /// <summary>Existing patient ID — required when <see cref="Mode"/> is "link".</summary>
    Guid? PatientId,

    /// <summary>New patient full name — required when <see cref="Mode"/> is "create".</summary>
    string? Name,

    /// <summary>Contact phone number in E.164 format — optional when <see cref="Mode"/> is "create".</summary>
    string? ContactNumber,

    /// <summary>New patient email — required when <see cref="Mode"/> is "create"; triggers 409 if duplicate.</summary>
    string? Email,

    /// <summary>Target specialty for the appointment — always required.</summary>
    Guid SpecialtyId,

    /// <summary>Appointment date — must be today or a future date.</summary>
    DateOnly Date,

    /// <summary>Requested time slot start — optional; when null or fully booked, appointment is queued-only.</summary>
    TimeOnly? TimeSlotStart,

    /// <summary>Requested time slot end — optional; should match <see cref="TimeSlotStart"/> when provided.</summary>
    TimeOnly? TimeSlotEnd);

/// <summary>
/// Response returned by <c>POST /api/staff/walkin</c> (US_026, AC-2, AC-3).
/// </summary>
public sealed record WalkInResponseDto(
    /// <summary>Primary key of the created <c>Appointment</c> record.</summary>
    Guid AppointmentId,

    /// <summary>
    /// UUID generated for anonymous walk-in visits (US_026, AC-3). <c>null</c> when
    /// the appointment is linked to a known patient.
    /// </summary>
    Guid? AnonymousVisitId,

    /// <summary>
    /// <c>true</c> when the requested time slot was fully booked; the appointment was created
    /// without a time slot and queued at the end of the same-day queue (edge case spec).
    /// </summary>
    bool QueuedOnly,

    /// <summary>Queue position assigned to this visit in the same-day queue.</summary>
    int QueuePosition);

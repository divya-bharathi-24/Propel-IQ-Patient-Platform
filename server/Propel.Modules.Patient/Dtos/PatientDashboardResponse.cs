namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// API response DTO for <c>GET /api/patient/dashboard</c> (US_016, AC-1, AC-2, AC-3, AC-4).
/// Aggregates upcoming appointments, clinical document history, and 360° view-verified status
/// for the authenticated patient into a single response (one request per page load — NFR-001).
/// <para>
/// <c>HasEmailDeliveryFailure</c> is <c>true</c> when a booking confirmation email delivery
/// failed after all automated retry attempts, signalling that a dismissable alert should be
/// displayed on the dashboard (US_021, AC-4).
/// </para>
/// </summary>
public sealed record PatientDashboardResponse(
    IReadOnlyList<UpcomingAppointmentDto> UpcomingAppointments,
    IReadOnlyList<DocumentHistoryDto> Documents,
    bool ViewVerified,
    bool HasEmailDeliveryFailure);

/// <summary>
/// DTO for an upcoming appointment shown on the patient dashboard (US_016, AC-1, AC-2).
/// <para>
/// <c>HasPendingIntake</c> is <c>true</c> when no completed <c>IntakeRecord</c>
/// (i.e. <c>completedAt IS NULL</c>) exists for the appointment, indicating the patient
/// still needs to submit their intake form (AC-2).
/// </para>
/// </summary>
public sealed record UpcomingAppointmentDto(
    Guid Id,
    DateOnly Date,
    TimeOnly? TimeSlotStart,
    string Specialty,
    string Status,
    bool HasPendingIntake);

/// <summary>
/// DTO for a clinical document upload entry shown on the patient dashboard (US_016, AC-3).
/// All <c>ProcessingStatus</c> values are included — including <c>Failed</c> — so the client
/// can present a retry option for failed uploads.
/// </summary>
public sealed record DocumentHistoryDto(
    Guid Id,
    string FileName,
    DateTime UploadedAt,
    string ProcessingStatus);

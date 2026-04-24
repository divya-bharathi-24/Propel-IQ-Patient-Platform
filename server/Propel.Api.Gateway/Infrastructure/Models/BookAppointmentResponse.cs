using Propel.Domain.Enums;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Api.Gateway.Infrastructure.Models;

/// <summary>
/// Response returned by <c>POST /api/appointments/book</c> (US_052, AC-4, NFR-018).
/// Extends the booking confirmation with calendar sync degradation information so the
/// frontend can surface ICS download links or manual-review notices to the user.
/// <para>
/// <see cref="DegradationNotices"/> is an empty list when all external services are healthy.
/// <see cref="IcsDownloadAvailable"/> is <c>true</c> when calendar sync failed and the
/// ICS download endpoint (<c>GET /api/appointments/{id}/ics</c>) is available as a fallback.
/// </para>
/// </summary>
public sealed record BookAppointmentResponse(
    Guid AppointmentId,
    string ReferenceNumber,
    DateOnly Date,
    TimeOnly TimeSlotStart,
    string SpecialtyName,
    InsuranceValidationResult InsuranceStatus,
    bool IcsDownloadAvailable,
    IReadOnlyList<DegradationNotice> DegradationNotices);

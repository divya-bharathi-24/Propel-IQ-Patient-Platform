using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="GetPatientDashboardQuery"/> for <c>GET /api/patient/dashboard</c> (US_016).
/// <list type="number">
///   <item>Delegates data retrieval to <see cref="IPatientDashboardRepository.GetDashboardAsync"/>,
///         which performs three EF Core projection queries in sequence:
///         (A) upcoming appointments with correlated pending-intake flag,
///         (B) clinical document upload history, and
///         (C) 360° view-verified status from <c>patients.view_verified_at</c>.</item>
///   <item>Maps domain read models to API response DTOs.</item>
///   <item>Returns an immutable <see cref="PatientDashboardResponse"/> record.</item>
/// </list>
/// <para>
/// No Redis cache is applied here. Dashboard data is patient-specific and expected to change
/// on each login session. If profiling reveals p95 &gt; 2 s under load, a short-lived
/// per-patient Redis cache (30 s TTL) can be added as a delta task (NFR-001, NFR-010).
/// </para>
/// </summary>
public sealed class GetPatientDashboardQueryHandler
    : IRequestHandler<GetPatientDashboardQuery, PatientDashboardResponse>
{
    private readonly IPatientDashboardRepository _dashboardRepo;
    private readonly ILogger<GetPatientDashboardQueryHandler> _logger;

    public GetPatientDashboardQueryHandler(
        IPatientDashboardRepository dashboardRepo,
        ILogger<GetPatientDashboardQueryHandler> logger)
    {
        _dashboardRepo = dashboardRepo;
        _logger = logger;
    }

    public async Task<PatientDashboardResponse> Handle(
        GetPatientDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var data = await _dashboardRepo.GetDashboardAsync(request.PatientId, cancellationToken);
        var hasEmailDeliveryFailure = await _dashboardRepo.HasEmailDeliveryFailureAsync(request.PatientId, cancellationToken);

        _logger.LogInformation(
            "Dashboard retrieved for PatientId {PatientId}: {AppointmentCount} appointments, " +
            "{DocumentCount} documents, ViewVerified={ViewVerified}, HasEmailDeliveryFailure={HasEmailDeliveryFailure}",
            request.PatientId,
            data.UpcomingAppointments.Count,
            data.Documents.Count,
            data.ViewVerified,
            hasEmailDeliveryFailure);

        // Map domain read models to API response DTOs.
        // Property names differ slightly (SpecialtyName → Specialty) to match the API contract.
        var appointments = data.UpcomingAppointments
            .Select(a => new UpcomingAppointmentDto(
                a.Id,
                a.Date,
                a.TimeSlotStart,
                a.SpecialtyName,
                a.Status,
                a.HasPendingIntake))
            .ToList()
            .AsReadOnly();

        var documents = data.Documents
            .Select(d => new DocumentHistoryDto(
                d.Id,
                d.FileName,
                d.UploadedAt,
                d.ProcessingStatus))
            .ToList()
            .AsReadOnly();

        return new PatientDashboardResponse(appointments, documents, data.ViewVerified, hasEmailDeliveryFailure);
    }
}

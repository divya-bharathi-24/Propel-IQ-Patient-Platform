using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Queries;

namespace Propel.Modules.Notification.Handlers;

/// <summary>
/// Handles <see cref="GetStaffAppointmentDetailQuery"/> for
/// <c>GET /api/staff/appointments/{id}</c> (US_034, AC-3).
/// <list type="number">
///   <item><b>Step 1 — Load appointment</b> with Patient and Specialty via
///         <see cref="IAppointmentBookingRepository.GetByIdWithPatientAsync"/> (read-only, AsNoTracking).</item>
///   <item><b>Step 2 — Query latest manual reminder</b> via
///         <see cref="INotificationRepository.GetLatestManualReminderAsync"/> — no time constraint,
///         returns the most recent <c>Sent</c> + <c>TriggeredBy IS NOT NULL</c> record.</item>
///   <item><b>Step 3 — Resolve staff name</b> via <see cref="IUserRepository.GetByIdAsync"/>
///         when a manual reminder record exists.</item>
///   <item><b>Step 4 — Project to <see cref="AppointmentDetailDto"/></b> with
///         <c>lastManualReminder</c> populated or <c>null</c> when no manual reminder exists yet.</item>
/// </list>
/// No PHI is written to Serilog structured log values (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class GetStaffAppointmentDetailQueryHandler
    : IRequestHandler<GetStaffAppointmentDetailQuery, AppointmentDetailDto>
{
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly INotificationRepository _notificationRepo;
    private readonly IUserRepository _userRepo;
    private readonly ILogger<GetStaffAppointmentDetailQueryHandler> _logger;

    public GetStaffAppointmentDetailQueryHandler(
        IAppointmentBookingRepository appointmentRepo,
        INotificationRepository notificationRepo,
        IUserRepository userRepo,
        ILogger<GetStaffAppointmentDetailQueryHandler> logger)
    {
        _appointmentRepo  = appointmentRepo;
        _notificationRepo = notificationRepo;
        _userRepo         = userRepo;
        _logger           = logger;
    }

    public async Task<AppointmentDetailDto> Handle(
        GetStaffAppointmentDetailQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load appointment with Patient and Specialty navigation properties.
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(
            request.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "GetStaffAppointmentDetail_NotFound: AppointmentId={AppointmentId}",
                request.AppointmentId);
            throw new KeyNotFoundException($"Appointment '{request.AppointmentId}' was not found.");
        }

        // Step 2 — Query latest manual reminder (no time constraint).
        var latestReminder = await _notificationRepo.GetLatestManualReminderAsync(
            request.AppointmentId, cancellationToken);

        // Step 3 — Resolve staff name when a reminder record exists.
        LastManualReminderDto? lastManualReminder = null;

        if (latestReminder is not null && latestReminder.SentAt is not null)
        {
            var staffName = "Staff";
            if (latestReminder.TriggeredBy.HasValue)
            {
                var staffUser = await _userRepo.GetByIdAsync(
                    latestReminder.TriggeredBy.Value, cancellationToken);
                staffName = staffUser?.Name ?? "Staff";
            }

            lastManualReminder = new LastManualReminderDto(
                SentAt:               new DateTimeOffset(latestReminder.SentAt.Value, TimeSpan.Zero),
                TriggeredByStaffName: staffName);
        }

        // Step 4 — Resolve specialty name.
        var specialtyName = await _appointmentRepo.GetSpecialtyNameAsync(
            appointment.SpecialtyId, cancellationToken);

        _logger.LogDebug(
            "GetStaffAppointmentDetail: AppointmentId={AppointmentId} hasReminder={HasReminder}",
            request.AppointmentId, lastManualReminder is not null);

        // Step 5 — Project to DTO.
        return new AppointmentDetailDto(
            AppointmentId:      appointment.Id,
            PatientName:        appointment.Patient?.Name ?? "Walk-In Guest",
            SpecialtyName:      specialtyName ?? "General",
            Date:               appointment.Date,
            TimeSlotStart:      appointment.TimeSlotStart,
            TimeSlotEnd:        appointment.TimeSlotEnd,
            Status:             appointment.Status.ToString(),
            LastManualReminder: lastManualReminder);
    }
}

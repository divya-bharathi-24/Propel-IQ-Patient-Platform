using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Queries;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="GetMyWaitlistQuery"/> for <c>GET /api/waitlist/me</c> (US_023, AC-2, AC-3).
/// <list type="number">
///   <item><b>Step 1 — Query WaitlistEntries</b>: fetches Active entries for the patient ordered by
///         <c>enrolledAt</c> ascending (FIFO — AC-2). Returns empty list, not 404, when none exist.</item>
///   <item><b>Step 2 — Map to DTOs</b>: projects each entity to <see cref="WaitlistEntryDto"/>.</item>
/// </list>
/// <para>
/// <c>PatientId</c> is always sourced from the JWT <c>sub</c> claim by the controller (OWASP A01).
/// </para>
/// </summary>
public sealed class GetMyWaitlistQueryHandler
    : IRequestHandler<GetMyWaitlistQuery, IReadOnlyList<WaitlistEntryDto>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<GetMyWaitlistQueryHandler> _logger;

    public GetMyWaitlistQueryHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<GetMyWaitlistQueryHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WaitlistEntryDto>> Handle(
        GetMyWaitlistQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Fetch Active entries ordered by enrolledAt ASC (FIFO — AC-2).
        var entries = await _waitlistRepo.GetActiveByPatientIdAsync(
            request.PatientId, cancellationToken);

        _logger.LogDebug(
            "GetMyWaitlist: PatientId={PatientId} ActiveEntries={Count}",
            request.PatientId, entries.Count);

        // Step 2 — Map entities to DTOs.
        return entries
            .Select(e => new WaitlistEntryDto(
                Id: e.Id,
                CurrentAppointmentId: e.CurrentAppointmentId,
                PreferredDate: e.PreferredDate,
                PreferredTimeSlot: e.PreferredTimeSlot,
                EnrolledAt: e.EnrolledAt,
                Status: e.Status.ToString()))
            .ToList();
    }
}

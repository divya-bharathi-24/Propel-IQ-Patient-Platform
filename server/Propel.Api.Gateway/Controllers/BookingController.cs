using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Infrastructure;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Handles appointment slot reservation and booking for the multi-step booking wizard
/// (US_019, AC-2, AC-3; US_052, AC-4). All endpoints require <c>Patient</c> role.
/// <para>
/// <c>patientId</c> is NEVER accepted from the request body — it is always resolved from
/// JWT claims inside the command handlers (OWASP A01 — Broken Access Control).
/// </para>
/// <para>
/// Calendar sync is attempted after booking and degrades gracefully (NFR-018):
/// a failed sync never blocks the booking confirmation; a <c>DegradationNotice</c> is included
/// in the response and <c>IcsDownloadAvailable</c> is set to <c>true</c> so the frontend can
/// offer the ICS download fallback (<c>GET /api/appointments/{id}/ics</c>).
/// </para>
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Roles = "Patient")]
public sealed class BookingController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly ICalendarSyncService _googleCalendarSync;
    private readonly ICalendarSyncService _outlookCalendarSync;

    public BookingController(
        ISender mediator,
        [FromKeyedServices("Google")] ICalendarSyncService googleCalendarSync,
        [FromKeyedServices("Outlook")] ICalendarSyncService outlookCalendarSync)
    {
        _mediator = mediator;
        _googleCalendarSync = googleCalendarSync;
        _outlookCalendarSync = outlookCalendarSync;
    }

    /// <summary>
    /// Places a 5-minute Redis slot-hold for the selected slot during the booking wizard
    /// (US_019, AC-2). Prevents the slot from appearing available to concurrent users.
    /// <para>
    /// Key format: <c>slot_hold:{specialtyId}:{date}:{timeSlot}:{patientId}</c>, TTL = 300 s.
    /// Redis failures are swallowed — this endpoint always returns 200 (NFR-018).
    /// </para>
    /// </summary>
    [HttpPost("hold-slot")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> HoldSlot(
        [FromBody] HoldSlotCommand command,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Commits the appointment booking (US_019, AC-2, AC-3; US_052, AC-4).
    /// <list type="number">
    ///   <item>Clears the Redis slot-hold.</item>
    ///   <item>INSERTs an <c>Appointment</c> (status = Booked); returns 409 on concurrent slot conflict.</item>
    ///   <item>Performs inline insurance soft-check and INSERTs <c>InsuranceValidation</c>.</item>
    ///   <item>Conditionally INSERTs a <c>WaitlistEntry</c> when <c>preferredSlotId</c> is provided.</item>
    ///   <item>Invalidates the slot availability cache and writes an audit log entry.</item>
    ///   <item>Attempts Google Calendar sync (then Outlook) after booking; failure never blocks confirmation (NFR-018).</item>
    /// </list>
    /// Returns 409 with <c>{"code":"SLOT_CONFLICT","message":"Slot no longer available"}</c>
    /// when two patients book the same slot concurrently (AC-3).
    /// When calendar sync fails, the response includes a <c>DegradationNotice</c> and
    /// <c>IcsDownloadAvailable = true</c> so the frontend can offer the ICS download fallback.
    /// </summary>
    [HttpPost("book")]
    [ProducesResponseType(typeof(BookAppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Book(
        [FromBody] CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var booking = await _mediator.Send(command, cancellationToken);

        // Resolve patientId from JWT (OWASP A01 — never from request body).
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(patientIdStr, out var patientId))
            return Unauthorized();

        // Attempt calendar sync — try Google first, then Outlook (NFR-018 graceful degradation).
        // null means the patient has not connected that provider; no DegradationNotice is raised.
        CalendarSyncResult? calSyncResult =
            await _googleCalendarSync.SyncAsync(booking.AppointmentId, patientId, cancellationToken)
            ?? await _outlookCalendarSync.SyncAsync(booking.AppointmentId, patientId, cancellationToken);

        // Map degradation results to notices for the frontend (US_052, AC-4).
        var notices = new List<DegradationNotice>();
        var calNotice = DegradationResponseFactory.FromCalendarSyncResult(calSyncResult);
        if (calNotice is not null)
            notices.Add(calNotice);

        return Ok(new BookAppointmentResponse(
            AppointmentId: booking.AppointmentId,
            ReferenceNumber: booking.ReferenceNumber,
            Date: booking.Date,
            TimeSlotStart: booking.TimeSlotStart,
            SpecialtyName: booking.SpecialtyName,
            InsuranceStatus: booking.InsuranceStatus,
            IcsDownloadAvailable: calSyncResult is CalendarSyncResult.Failed,
            DegradationNotices: notices));
    }
}

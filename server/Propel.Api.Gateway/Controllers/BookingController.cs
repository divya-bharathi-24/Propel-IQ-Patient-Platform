using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Dtos;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Handles appointment slot reservation and booking for the multi-step booking wizard
/// (US_019, AC-2, AC-3). All endpoints require <c>Patient</c> role.
/// <para>
/// <c>patientId</c> is NEVER accepted from the request body — it is always resolved from
/// JWT claims inside the command handlers (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Roles = "Patient")]
public sealed class BookingController : ControllerBase
{
    private readonly ISender _mediator;

    public BookingController(ISender mediator)
    {
        _mediator = mediator;
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
    /// Commits the appointment booking (US_019, AC-2, AC-3).
    /// <list type="number">
    ///   <item>Clears the Redis slot-hold.</item>
    ///   <item>INSERTs an <c>Appointment</c> (status = Booked); returns 409 on concurrent slot conflict.</item>
    ///   <item>Performs inline insurance soft-check and INSERTs <c>InsuranceValidation</c>.</item>
    ///   <item>Conditionally INSERTs a <c>WaitlistEntry</c> when <c>preferredSlotId</c> is provided.</item>
    ///   <item>Invalidates the slot availability cache and writes an audit log entry.</item>
    /// </list>
    /// Returns 409 with <c>{"code":"SLOT_CONFLICT","message":"Slot no longer available"}</c>
    /// when two patients book the same slot concurrently (AC-3).
    /// </summary>
    [HttpPost("book")]
    [ProducesResponseType(typeof(BookingResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Book(
        [FromBody] CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}

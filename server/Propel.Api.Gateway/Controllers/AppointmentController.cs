using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Queries;
using Propel.Api.Gateway.Infrastructure.Security;

namespace Propel.Api.Gateway.Controllers;

[ApiController]
[Route("api/appointments")]
public sealed class AppointmentController : ControllerBase
{
    private readonly IMediator _mediator;

    public AppointmentController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("ping")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PingAppointmentCommand(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns all available medical specialties (US_018, AC-1).
    /// Used by the booking wizard to populate the specialty dropdown before slot selection.
    /// </summary>
    [HttpGet("specialties")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(IReadOnlyList<SpecialtyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSpecialties(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSpecialtiesQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns available appointment slots for the given specialty and date (US_018, AC-1, AC-4).
    /// <para>
    /// Response is served from Redis cache (TTL 5 s, NFR-020). On cache miss or Redis failure
    /// the handler falls back to PostgreSQL and logs a Serilog <c>Warning "SlotCache_Miss"</c>
    /// event (AC-3). Slots with <c>IsAvailable = false</c> are held by <c>Booked</c> or
    /// <c>Arrived</c> appointments; when all slots are unavailable the frontend infers the
    /// fully-booked state without a separate status code (AC-4).
    /// </para>
    /// </summary>
    /// <param name="specialtyId">Non-empty GUID identifying the medical specialty.</param>
    /// <param name="date">Today or a future date in ISO-8601 format (YYYY-MM-DD).</param>
    [HttpGet("slots")]
    [Authorize(Roles = "Patient")]
    [EnableRateLimiting(RateLimitingPolicies.SlotsQuery)]
    [ProducesResponseType(typeof(SlotAvailabilityResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] Guid specialtyId,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        var query = new GetAvailableSlotsQuery(specialtyId, date);
        var result = await _mediator.Send(query, cancellationToken);
        // Return the flat Slots array so the frontend receives SlotDto[] directly.
        return Ok(result.Slots);
    }

    /// <summary>
    /// Cancels an appointment owned by the authenticated patient (US_020, AC-1, AC-2, AC-4).
    /// <list type="number">
    ///   <item>Sets <c>Appointment.status = Cancelled</c> and records the optional <c>cancellationReason</c>.</item>
    ///   <item>Suppresses all <c>Pending</c> <c>Notification</c> records for the appointment.</item>
    ///   <item>Cancels any <c>Active</c> <c>WaitlistEntry</c> linked to the appointment.</item>
    ///   <item>Invalidates the Redis slot cache so the freed slot is immediately visible to other patients.</item>
    ///   <item>Queues a fire-and-forget task to revoke the external calendar event (Google / Outlook).</item>
    ///   <item>Writes an immutable audit log entry.</item>
    /// </list>
    /// <para>
    /// Returns HTTP 400 when the appointment date is in the past, HTTP 403 when the patient
    /// does not own the appointment, and HTTP 404 when the appointment does not exist.
    /// </para>
    /// <para>
    /// <c>PatientId</c> is NEVER accepted from the request body — it is resolved from the JWT
    /// <c>sub</c> claim inside the handler (OWASP A01 — Broken Access Control).
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelAppointment(
        [FromRoute] Guid id,
        [FromBody] CancelAppointmentRequestDto? body,
        CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT sub claim — never from request body (OWASP A01).
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var command = new CancelAppointmentCommand(
            AppointmentId: id,
            PatientId: patientId,
            CancellationReason: body?.CancellationReason);

        await _mediator.Send(command, cancellationToken);

        return Ok(new { message = "Appointment cancelled" });
    }

    /// <summary>
    /// Reschedules an appointment owned by the authenticated patient (US_020, AC-3, task_003).
    /// <list type="number">
    ///   <item>Sets original <c>Appointment.status = Cancelled</c> and suppresses its <c>Pending</c> <c>Notification</c> records.</item>
    ///   <item>Cancels any <c>Active</c> <c>WaitlistEntry</c> linked to the original appointment.</item>
    ///   <item>Creates a new <c>Appointment</c> record (status = <c>Booked</c>) for the selected slot.</item>
    ///   <item>Both mutations are committed atomically in a single <c>SaveChangesAsync()</c> call.</item>
    ///   <item>Invalidates the Redis slot cache for both the original and new slot dates.</item>
    ///   <item>Queues a fire-and-forget calendar sync revocation for the original appointment.</item>
    ///   <item>Triggers a PDF confirmation email to the patient (must arrive within 60 seconds — AC-3).</item>
    ///   <item>Writes an immutable audit log entry.</item>
    /// </list>
    /// <para>
    /// Returns HTTP 400 when the original appointment date is in the past, HTTP 403 when the
    /// patient does not own the appointment, HTTP 404 when the appointment does not exist, and
    /// HTTP 409 when the new slot is already taken by another booking.
    /// </para>
    /// <para>
    /// <c>PatientId</c> is NEVER accepted from the request body — it is resolved from the JWT
    /// <c>sub</c> claim (OWASP A01 — Broken Access Control).
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(RescheduleAppointmentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RescheduleAppointment(
        [FromRoute] Guid id,
        [FromBody] RescheduleAppointmentRequestDto body,
        CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT sub claim — never from request body (OWASP A01).
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var command = new RescheduleAppointmentCommand(
            OriginalAppointmentId: id,
            PatientId: patientId,
            NewDate: body.NewDate,
            NewTimeSlotStart: body.NewTimeSlotStart,
            NewTimeSlotEnd: body.NewTimeSlotEnd,
            SpecialtyId: body.SpecialtyId);

        var result = await _mediator.Send(command, cancellationToken);

        return Ok(new
        {
            newAppointmentId = result.NewAppointmentId,
            confirmationNumber = result.ConfirmationNumber
        });
    }
}

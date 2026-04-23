using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Enums;
using Propel.Modules.Admin.Queries;
using Propel.Modules.Notification.Commands;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Queries;
using Propel.Modules.Risk.Dtos;
using Propel.Modules.Risk.Queries;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Staff-exclusive endpoints for patient search and walk-in booking (US_026, AC-1 – AC-4).
/// Controller-level <c>[Authorize(Roles = "Staff")]</c> rejects Patient-role JWTs with HTTP 403
/// before any handler logic executes (AC-4, FR-024).
/// </summary>
[ApiController]
[Route("api/staff")]
[Authorize(Roles = "Staff")]
public sealed class StaffController : ControllerBase
{
    private readonly IMediator _mediator;

    public StaffController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Searches patients by name fragment or date of birth (US_026, AC-1).
    /// Returns up to 20 <see cref="PatientSearchResultDto"/> records.
    /// Requires a minimum of 2 characters to avoid broad table scans.
    /// </summary>
    /// <param name="query">Name fragment or date-of-birth string (minimum 2 characters).</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpGet("patients/search")]
    [ProducesResponseType(typeof(IReadOnlyList<PatientSearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchPatients(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return BadRequest(new { message = "'query' must be at least 2 characters." });

        var result = await _mediator.Send(
            new SearchPatientsQuery(query.Trim()), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Creates a walk-in appointment with optional patient creation (US_026, AC-2, AC-3).
    /// <list type="bullet">
    ///   <item><c>mode = link</c>: links appointment to an existing patient.</item>
    ///   <item><c>mode = create</c>: creates a new patient record then links appointment; HTTP 409 on duplicate email.</item>
    ///   <item><c>mode = anonymous</c>: creates appointment with no patient link and a generated <c>anonymousVisitId</c>.</item>
    /// </list>
    /// Always inserts a <c>QueueEntry</c> (status = Waiting). If the requested time slot is fully booked,
    /// the appointment is created without a time slot and <c>queuedOnly = true</c> is returned.
    /// </summary>
    /// <param name="dto">Walk-in booking request body.</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpPost("walkin")]
    [ProducesResponseType(typeof(WalkInResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateWalkIn(
        [FromBody] WalkInBookingDto dto,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<WalkInMode>(dto.Mode, ignoreCase: true, out var mode))
        {
            return BadRequest(new
            {
                message = "'mode' must be one of: link, create, anonymous."
            });
        }

        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        var staffId = GetCurrentUserId();

        var command = new CreateWalkInCommand(
            mode,
            dto.PatientId,
            dto.Name,
            dto.ContactNumber,
            dto.Email,
            dto.SpecialtyId,
            dto.Date,
            dto.TimeSlotStart,
            dto.TimeSlotEnd,
            staffId,
            ipAddress,
            correlationId);

        var result = await _mediator.Send(command, cancellationToken);

        return CreatedAtAction(
            nameof(CreateWalkIn),
            new { appointmentId = result.AppointmentId },
            result);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Returns appointments for the specified date with embedded no-show risk data (us_031, AC-1).
    /// Accessible only by Staff-role JWT bearers (OWASP A01 — Broken Access Control).
    /// </summary>
    /// <param name="date">Calendar date in <c>yyyy-MM-dd</c> format (required).</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpGet("appointments")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffAppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string date,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out var parsedDate))
            return BadRequest(new { message = "'date' must be a valid date in yyyy-MM-dd format." });

        var result = await _mediator.Send(
            new GetStaffAppointmentsQuery(parsedDate), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns the appointment detail for a single appointment (US_034, AC-3).
    /// Includes the <c>lastManualReminder</c> projection (<c>sentAt</c>, <c>triggeredByStaffName</c>)
    /// for confirmation display in the staff UI after a manual reminder trigger.
    /// </summary>
    [HttpGet("appointments/{id:guid}")]
    [ProducesResponseType(typeof(AppointmentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAppointmentDetail(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetStaffAppointmentDetailQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Triggers an immediate manual reminder for the specified appointment (US_034, AC-1–AC-4).
    /// <list type="bullet">
    ///   <item>Returns HTTP 422 when the appointment is <c>Cancelled</c>.</item>
    ///   <item>Returns HTTP 429 with <c>retryAfterSeconds</c> when a reminder was sent within
    ///         the last 5 minutes (debounce cooldown).</item>
    ///   <item>Returns HTTP 200 with <c>emailErrorReason</c> / <c>smsErrorReason</c> when a
    ///         channel fails — delivery errors do NOT produce a 5xx (AC-4).</item>
    /// </list>
    /// <c>StaffUserId</c> is resolved from the JWT <c>sub</c> claim — never from the request body
    /// (OWASP A01 — Broken Access Control).
    /// </summary>
    [HttpPost("appointments/{appointmentId:guid}/reminders/trigger")]
    [ProducesResponseType(typeof(TriggerManualReminderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TriggerManualReminder(
        [FromRoute] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        // StaffUserId resolved from JWT sub claim — never from request body (OWASP A01).
        var staffUserId = GetCurrentUserId();
        var result = await _mediator.Send(
            new TriggerManualReminderCommand(appointmentId, staffUserId), cancellationToken);
        return Ok(result);
    }
}

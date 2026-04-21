using MediatR;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Appointment.Commands;

namespace Propel.Api.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
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
}

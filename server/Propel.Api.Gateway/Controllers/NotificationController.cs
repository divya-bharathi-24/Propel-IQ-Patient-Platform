using MediatR;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Notification.Commands;

namespace Propel.Api.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class NotificationController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("ping")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PingNotificationCommand(), cancellationToken);
        return Ok(result);
    }
}

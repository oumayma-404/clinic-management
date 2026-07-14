using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Notifications.Commands;
using ClinicManagement.Application.Features.Notifications.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// List the current user's clinic notifications for the panel (newest first, most recent 50),
    /// each annotated with the current user's read state.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications()
    {
        var result = await _mediator.Send(new GetNotificationsQuery());

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The current user's total unread count for the bell badge (may exceed the 50 shown).
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult> GetUnreadCount()
    {
        var result = await _mediator.Send(new GetUnreadCountQuery());

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(new { unreadCount = result.Value });
    }

    /// <summary>
    /// Mark a single notification read for the current user.
    /// </summary>
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var result = await _mediator.Send(new MarkNotificationReadCommand { Id = id });

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return NoContent();
    }

    /// <summary>
    /// Mark all of the current user's unread notifications read.
    /// </summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var result = await _mediator.Send(new MarkAllNotificationsReadCommand());

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}

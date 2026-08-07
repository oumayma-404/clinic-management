using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Notifications.Commands;
using ClinicManagement.Application.Features.Notifications.Queries;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class NotificationsController : ApiControllerBase
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
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The current user's due, unread post-visit review notifications (drives the "how was the visit"
    /// popup; the frontend polls this periodically).
    /// </summary>
    [HttpGet("pending-reviews")]
    public async Task<ActionResult<IEnumerable<PendingReviewDto>>> GetPendingReviews()
    {
        var result = await _mediator.Send(new GetPendingReviewsQuery());

        if (result.IsFailure)
        {
            return HandleFailure(result);
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
            return HandleFailure(result);
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
            // The only non-auth failure is the tenant-mismatch/missing case, which the command
            // treats as "not found" — surface it as 404 (matches AppointmentsController.GetAppointment).
            return HandleFailure(result, StatusCodes.Status404NotFound);
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
            return HandleFailure(result);
        }

        return NoContent();
    }
}

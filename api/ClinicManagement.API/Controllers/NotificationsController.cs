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
    [AllowsWithoutSubscription("FR-3 — otherwise the expiry notice itself could never be dismissed (AC-3.4).")]
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
    [AllowsWithoutSubscription("FR-3 — clearing the bell is reading, and it is where the warnings arrive.")]
    public async Task<IActionResult> MarkAllRead()
    {
        var result = await _mediator.Send(new MarkAllNotificationsReadCommand());

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }

    /// <summary>
    /// Remove a single notification from the current user's own bell.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>DELETE</c> is the verb the caller sees, and it is honest about the effect on <i>their</i> feed — but
    /// nothing is deleted for the cabinet: a notification row is shared with every colleague it targets, so this
    /// writes a per-user dismissal marker. See <c>DismissNotificationCommand</c>.
    /// </remarks>
    [HttpDelete("{id}")]
    [AllowsWithoutSubscription("FR-3 — same reasoning as MarkRead: the expiry notice arrives here too.")]
    public async Task<IActionResult> Dismiss(Guid id)
    {
        var result = await _mediator.Send(new DismissNotificationCommand { Id = id });

        if (result.IsFailure)
        {
            // As with MarkRead, the only non-auth failure is the tenant-mismatch/missing case.
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return NoContent();
    }

    /// <summary>
    /// Empty the current user's own bell.
    /// </summary>
    [HttpDelete]
    [AllowsWithoutSubscription("FR-3 — same reasoning as MarkAllRead.")]
    public async Task<IActionResult> DismissAll()
    {
        var result = await _mediator.Send(new DismissAllNotificationsCommand());

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }
}

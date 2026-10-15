using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>« Messages » — the cabinet's internal chat (staff-chat). Every role; a thread is private to its members.</summary>
[ApiController]
[Route("api/staff-chat")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class StaffChatController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public StaffChatController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<List<StaffConversationDto>>> GetConversations()
    {
        var result = await _mediator.Send(new GetStaffConversationsQuery());
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpGet("colleagues")]
    public async Task<ActionResult<List<StaffColleagueDto>>> GetColleagues()
    {
        var result = await _mediator.Send(new GetStaffColleaguesQuery());
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpGet("messages")]
    public async Task<ActionResult<StaffMessagePageDto>> GetMessages(
        [FromQuery] string conversation, [FromQuery] string? before, [FromQuery] int? take)
    {
        var result = await _mediator.Send(new GetStaffMessagesQuery { Conversation = conversation, Before = before, Take = take });
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpGet("unread")]
    public async Task<ActionResult<StaffChatUnreadDto>> GetUnread()
    {
        var result = await _mediator.Send(new GetStaffChatUnreadQuery());
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpPost("messages")]
    public async Task<ActionResult<StaffMessageDto>> Send([FromBody] SendStaffMessageCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? Refuse(result) : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("quick-messages")]
    public async Task<ActionResult<StaffQuickMessageListDto>> GetQuickMessages()
    {
        var result = await _mediator.Send(new GetStaffQuickMessagesQuery());
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpPut("quick-messages")]
    public async Task<ActionResult<StaffQuickMessageListDto>> SaveQuickMessages([FromBody] SaveStaffQuickMessagesCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpDelete("quick-messages")]
    public async Task<ActionResult<StaffQuickMessageListDto>> ResetQuickMessages()
    {
        var result = await _mediator.Send(new ResetStaffQuickMessagesCommand());
        return result.IsFailure ? Refuse(result) : Ok(result.Value);
    }

    [HttpPost("conversations/read")]
    [AllowsWithoutSubscription("Marking a thread read records what one person has seen, not new work — an expired cabinet still reads its messages.")]
    public async Task<IActionResult> MarkRead([FromBody] MarkStaffConversationReadCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsFailure ? Refuse(result) : NoContent();
    }

    private ActionResult Refuse(Result result) => HandleFailure(result, result.Code switch
    {
        StaffChatRefusals.ConversationNotFoundCode or StaffChatRefusals.MessageNotFoundCode => StatusCodes.Status404NotFound,
        StaffChatRefusals.RecipientInactiveCode => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    });
}

using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat.Commands;

/// <summary>Marks a thread read up to one of its messages, for the caller only; idempotent and never moves backwards.</summary>
public class MarkStaffConversationReadCommand : IRequest<Result>
{
    public string Conversation { get; set; } = string.Empty;
    public Guid UpToMessageId { get; set; }
}

public class MarkStaffConversationReadCommandHandler : IRequestHandler<MarkStaffConversationReadCommand, Result>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<MarkStaffConversationReadCommandHandler> _logger;

    public MarkStaffConversationReadCommandHandler(
        IStaffMessageRepository messages,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        ILogger<MarkStaffConversationReadCommandHandler> logger)
    {
        _messages = messages;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkStaffConversationReadCommand request, CancellationToken cancellationToken)
    {
        StaffChatRoster roster;
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded;
            }

            roster = loaded.Value!;
            var conversation = StaffChatInbox.Resolve(request.Conversation, roster);
            if (conversation is null)
            {
                return Result.Failure(StaffChatRefusals.ConversationNotFound, StaffChatRefusals.ConversationNotFoundCode);
            }

            var message = await _messages.GetByIdAsync(request.UpToMessageId, cancellationToken);
            if (message is null || message.ClinicId != roster.ClinicId || message.ConversationKey != conversation.Key)
            {
                return Result.Failure(StaffChatRefusals.MessageNotFound, StaffChatRefusals.MessageNotFoundCode);
            }

            var marker = await _messages.GetMarkerAsync(
                roster.ClinicId, roster.Viewer.UserId, conversation.Key, cancellationToken);
            if (marker is null)
            {
                await _messages.AddMarkerAsync(
                    new StaffChatReadMarker(roster.ClinicId, roster.Viewer.UserId, conversation.Key, message.SentAt),
                    cancellationToken);
            }
            else if (!marker.Advance(message.SentAt))
            {
                return Result.Success();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure("La conversation n'a pas pu être marquée comme lue.", ex);
        }

        try
        {
            // Only the reader's own tabs: the badge on their other devices clears (EC-9).
            await _realtime.NotifyStaffChatAsync(roster.ClinicId, new[] { roster.Viewer.UserId }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff chat read marker saved but the reader's other tabs were not signalled");
        }

        return Result.Success();
    }
}

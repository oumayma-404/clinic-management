using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat.Commands;

/// <summary>Sends a text into a thread the caller belongs to (staff-chat AC-2.3). A retry reuses the request's Idempotency-Key.</summary>
public class SendStaffMessageCommand : IRequest<Result<StaffMessageDto>>
{
    public string Conversation { get; set; } = string.Empty;
    public string? Body { get; set; }
}

public class SendStaffMessageCommandHandler : IRequestHandler<SendStaffMessageCommand, Result<StaffMessageDto>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<SendStaffMessageCommandHandler> _logger;

    public SendStaffMessageCommandHandler(
        IStaffMessageRepository messages,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        ILogger<SendStaffMessageCommandHandler> logger)
    {
        _messages = messages;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<StaffMessageDto>> Handle(SendStaffMessageCommand request, CancellationToken cancellationToken)
    {
        StaffMessage message;
        StaffChatRoster roster;
        IReadOnlyList<string> members;
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<StaffMessageDto>.FailureFrom(loaded);
            }

            roster = loaded.Value!;
            var conversation = StaffChatInbox.Resolve(request.Conversation, roster);
            if (conversation is null)
            {
                return Result<StaffMessageDto>.Failure(
                    StaffChatRefusals.ConversationNotFound, StaffChatRefusals.ConversationNotFoundCode);
            }

            if (StaffChatInbox.IsReadOnly(conversation, roster))
            {
                return Result<StaffMessageDto>.Failure(
                    StaffChatRefusals.RecipientInactive, StaffChatRefusals.RecipientInactiveCode);
            }

            var body = StaffMessage.NormalizeBody(request.Body);
            if (body is null)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.Empty, StaffChatRefusals.EmptyCode);
            }

            if (body.Length > StaffMessage.BodyMaxLength)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.TooLong, StaffChatRefusals.TooLongCode);
            }

            message = StaffMessage.Create(
                roster.ClinicId, conversation, roster.Viewer.UserId, body, null, null, null, DateTime.UtcNow);
            await _messages.AddAsync(message, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            members = conversation.Members(roster.Members);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffMessageDto>.Failure("Le message n'a pas pu être envoyé.", ex);
        }

        try
        {
            await _realtime.NotifyStaffChatAsync(roster.ClinicId, members, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff chat message {MessageId} saved but its members were not signalled", message.Id);
        }

        return Result<StaffMessageDto>.Success(StaffChatInbox.ToMessageDto(message, roster));
    }
}

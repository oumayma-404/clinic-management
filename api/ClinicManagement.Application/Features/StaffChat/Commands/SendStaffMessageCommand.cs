using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat.Commands;

/// <summary>
/// Sends a text or a quick message into a thread the caller belongs to (AC-2.1, AC-2.3), optionally about a patient
/// and one of their appointments (AC-2.4). A retry reuses the request's Idempotency-Key.
/// </summary>
public class SendStaffMessageCommand : IRequest<Result<StaffMessageDto>>
{
    public string Conversation { get; set; } = string.Empty;
    public string? Body { get; set; }
    /// <summary>A quick message of the cabinet's list; its label and urgency are kept as they are now (AC-6.4).</summary>
    public string? QuickMessageKey { get; set; }
    public Guid? PatientId { get; set; }
    /// <summary>One of that patient's appointments; sent alone, it brings its patient with it.</summary>
    public Guid? AppointmentId { get; set; }
}

public class SendStaffMessageCommandHandler : IRequestHandler<SendStaffMessageCommand, Result<StaffMessageDto>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IStaffQuickMessageListRepository _quickMessages;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly IStaffChatPushQueue _push;
    private readonly ILogger<SendStaffMessageCommandHandler> _logger;

    public SendStaffMessageCommandHandler(
        IStaffMessageRepository messages,
        IStaffQuickMessageListRepository quickMessages,
        IPatientRepository patients,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        IStaffChatPushQueue push,
        ILogger<SendStaffMessageCommandHandler> logger)
    {
        _messages = messages;
        _quickMessages = quickMessages;
        _patients = patients;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _push = push;
        _logger = logger;
    }

    public async Task<Result<StaffMessageDto>> Handle(SendStaffMessageCommand request, CancellationToken cancellationToken)
    {
        StaffMessage message;
        StaffChatRoster roster;
        StaffConversation conversation;
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<StaffMessageDto>.FailureFrom(loaded);
            }

            roster = loaded.Value!;
            var resolved = StaffChatInbox.Resolve(request.Conversation, roster);
            if (resolved is null)
            {
                return Result<StaffMessageDto>.Failure(
                    StaffChatRefusals.ConversationNotFound, StaffChatRefusals.ConversationNotFoundCode);
            }

            conversation = resolved;
            if (StaffChatInbox.IsReadOnly(conversation, roster))
            {
                return Result<StaffMessageDto>.Failure(
                    StaffChatRefusals.RecipientInactive, StaffChatRefusals.RecipientInactiveCode);
            }

            StaffQuickMessageSnapshot? quick = null;
            if (!string.IsNullOrWhiteSpace(request.QuickMessageKey))
            {
                var list = await _quickMessages.GetByClinicIdAsync(roster.ClinicId, cancellationToken);
                quick = StaffQuickMessageCatalog.Effective(list)
                    .FirstOrDefault(i => i.Key == request.QuickMessageKey.Trim())?
                    .Snapshot();
                if (quick is null)
                {
                    return Result<StaffMessageDto>.Failure(
                        StaffChatRefusals.UnknownQuickMessage, StaffChatRefusals.UnknownQuickMessageCode);
                }
            }

            var body = StaffMessage.NormalizeBody(request.Body);
            if (body is null && quick is null)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.Empty, StaffChatRefusals.EmptyCode);
            }

            if (body?.Length > StaffMessage.BodyMaxLength)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.TooLong, StaffChatRefusals.TooLongCode);
            }

            var links = await StaffMessageLinks.ValidateAsync(
                request.PatientId, request.AppointmentId, roster, _patients, _messages, cancellationToken);
            if (links.IsFailure)
            {
                return Result<StaffMessageDto>.FailureFrom(links);
            }

            var (patientId, appointmentId) = links.Value;
            message = StaffMessage.Create(
                roster.ClinicId, conversation, roster.Viewer.UserId, body, quick, patientId, appointmentId, DateTime.UtcNow);
            await _messages.AddAsync(message, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffMessageDto>.Failure("Le message n'a pas pu être envoyé.", ex);
        }

        return Result<StaffMessageDto>.Success(await StaffMessagePosted.AnnounceAsync(
            message, conversation, roster, _realtime, _push, _patients, _messages, _logger, cancellationToken));
    }
}

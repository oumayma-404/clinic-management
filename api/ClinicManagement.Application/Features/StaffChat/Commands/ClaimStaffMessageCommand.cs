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
/// « Je m'en occupe » (FR-6, AC-5.1): a secretary takes the owner's request for the whole secretariat. Two at once:
/// the message's version decides, and the loser is told who has it (EC-4) — never a second claim.
/// </summary>
public class ClaimStaffMessageCommand : IRequest<Result<StaffMessageDto>>
{
    public Guid MessageId { get; set; }
}

public class ClaimStaffMessageCommandHandler : IRequestHandler<ClaimStaffMessageCommand, Result<StaffMessageDto>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<ClaimStaffMessageCommandHandler> _logger;

    public ClaimStaffMessageCommandHandler(
        IStaffMessageRepository messages,
        IPatientRepository patients,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        ILogger<ClaimStaffMessageCommandHandler> logger)
    {
        _messages = messages;
        _patients = patients;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<StaffMessageDto>> Handle(ClaimStaffMessageCommand request, CancellationToken cancellationToken)
    {
        StaffChatRoster roster;
        StaffMessage message;
        StaffConversation conversation;
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<StaffMessageDto>.FailureFrom(loaded);
            }

            roster = loaded.Value!;
            var viewer = roster.Viewer.UserId;
            var found = await StaffClaimTarget.FindAsync(_messages, request.MessageId, roster, cancellationToken);
            if (found is null)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.MessageNotFound, StaffChatRefusals.MessageNotFoundCode);
            }

            (message, conversation) = found.Value;
            if (!conversation.OffersClaim(message.SenderUserId, viewer, roster.Members))
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.NotClaimable, StaffChatRefusals.NotClaimableCode);
            }

            if (message.ClaimedByUserId is { } holder && holder != viewer)
            {
                return AlreadyClaimed(holder, roster);
            }

            if (message.ClaimedByUserId == viewer)
            {
                return Result<StaffMessageDto>.Success(await StaffMessagePosted.DtoAsync(message, roster, _patients, _messages, _logger, cancellationToken));
            }

            message.Claim(viewer, DateTime.UtcNow);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ConflictException)
            {
                // Two secretaries pressed together: the other one's save landed first. Name them (EC-4).
                var winner = await _messages.GetClaimedByAsync(roster.ClinicId, message.Id, cancellationToken);
                if (winner is not null && winner != viewer)
                {
                    return AlreadyClaimed(winner, roster);
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffMessageDto>.Failure("Le message n'a pas pu être pris en charge.", ex);
        }

        await StaffClaimTarget.SignalAsync(_realtime, roster, conversation, message, _logger, cancellationToken);
        return Result<StaffMessageDto>.Success(await StaffMessagePosted.DtoAsync(message, roster, _patients, _messages, _logger, cancellationToken));
    }

    private static Result<StaffMessageDto> AlreadyClaimed(string holder, StaffChatRoster roster) =>
        Result<StaffMessageDto>.Failure(StaffChatRefusals.AlreadyClaimed(roster.NameOf(holder)), StaffChatRefusals.AlreadyClaimedCode);
}

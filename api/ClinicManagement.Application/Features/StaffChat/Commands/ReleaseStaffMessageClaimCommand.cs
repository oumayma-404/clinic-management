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

/// <summary>« Je ne peux plus » (AC-5.2): the taker hands the request back to the whole secretariat.</summary>
public class ReleaseStaffMessageClaimCommand : IRequest<Result<StaffMessageDto>>
{
    public Guid MessageId { get; set; }
}

public class ReleaseStaffMessageClaimCommandHandler : IRequestHandler<ReleaseStaffMessageClaimCommand, Result<StaffMessageDto>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<ReleaseStaffMessageClaimCommandHandler> _logger;

    public ReleaseStaffMessageClaimCommandHandler(
        IStaffMessageRepository messages,
        IPatientRepository patients,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        ILogger<ReleaseStaffMessageClaimCommandHandler> logger)
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

    public async Task<Result<StaffMessageDto>> Handle(ReleaseStaffMessageClaimCommand request, CancellationToken cancellationToken)
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
            var found = await StaffClaimTarget.FindAsync(_messages, request.MessageId, roster, cancellationToken);
            if (found is null)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.MessageNotFound, StaffChatRefusals.MessageNotFoundCode);
            }

            (message, conversation) = found.Value;
            if (message.ClaimedByUserId is null)
            {
                return Result<StaffMessageDto>.Success(await StaffMessagePosted.DtoAsync(message, roster, _patients, _messages, _logger, cancellationToken));
            }

            if (message.ClaimedByUserId != roster.Viewer.UserId)
            {
                return Result<StaffMessageDto>.Failure(StaffChatRefusals.NotYourClaim, StaffChatRefusals.NotYourClaimCode);
            }

            message.ReleaseClaim(roster.Viewer.UserId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffMessageDto>.Failure("Le message n'a pas pu être rendu.", ex);
        }

        await StaffClaimTarget.SignalAsync(_realtime, roster, conversation, message, _logger, cancellationToken);
        return Result<StaffMessageDto>.Success(await StaffMessagePosted.DtoAsync(message, roster, _patients, _messages, _logger, cancellationToken));
    }
}

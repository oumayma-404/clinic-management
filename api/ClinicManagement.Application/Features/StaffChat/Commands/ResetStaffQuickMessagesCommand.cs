using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat.Commands;

/// <summary>« Rétablir les messages par défaut » (AC-6.3): the saved list goes, the 17 defaults are back.</summary>
public class ResetStaffQuickMessagesCommand : IRequest<Result<StaffQuickMessageListDto>>
{
}

public class ResetStaffQuickMessagesCommandHandler : IRequestHandler<ResetStaffQuickMessagesCommand, Result<StaffQuickMessageListDto>>
{
    private readonly IStaffQuickMessageListRepository _lists;
    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<ResetStaffQuickMessagesCommandHandler> _logger;

    public ResetStaffQuickMessagesCommandHandler(
        IStaffQuickMessageListRepository lists,
        IStaffMessageRepository messages,
        IUserRepository users,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        ILogger<ResetStaffQuickMessagesCommandHandler> logger)
    {
        _lists = lists;
        _messages = messages;
        _users = users;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<StaffQuickMessageListDto>> Handle(ResetStaffQuickMessagesCommand request, CancellationToken cancellationToken)
    {
        Guid clinicId;
        string userId;
        try
        {
            var caller = await StaffChatRoster.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (caller.IsFailure)
            {
                return Result<StaffQuickMessageListDto>.FailureFrom(caller);
            }

            clinicId = caller.Value!.ClinicId;
            userId = caller.Value.Id;

            var existing = await _lists.GetByClinicIdAsync(clinicId, cancellationToken);
            if (existing is not null)
            {
                _lists.Remove(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffQuickMessageListDto>.Failure("Les messages par défaut n'ont pas pu être rétablis.", ex);
        }

        await StaffQuickMessageSignal.SendAsync(_realtime, clinicId, _logger, cancellationToken);
        return Result<StaffQuickMessageListDto>.Success(await StaffQuickMessageCatalog.BuildAsync(
            null, clinicId, userId, _messages, DateTime.UtcNow, cancellationToken));
    }
}

using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.StaffChat.Queries;

/// <summary>The cabinet's quick messages, ordered for the caller (AC-2.1, AC-2.2).</summary>
public class GetStaffQuickMessagesQuery : IRequest<Result<StaffQuickMessageListDto>>
{
}

public class GetStaffQuickMessagesQueryHandler : IRequestHandler<GetStaffQuickMessagesQuery, Result<StaffQuickMessageListDto>>
{
    private readonly IStaffQuickMessageListRepository _lists;
    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;

    public GetStaffQuickMessagesQueryHandler(
        IStaffQuickMessageListRepository lists,
        IStaffMessageRepository messages,
        IUserRepository users,
        IClinicContext clinicContext)
    {
        _lists = lists;
        _messages = messages;
        _users = users;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffQuickMessageListDto>> Handle(GetStaffQuickMessagesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var caller = await StaffChatRoster.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (caller.IsFailure)
            {
                return Result<StaffQuickMessageListDto>.FailureFrom(caller);
            }

            var user = caller.Value!;
            var list = await _lists.GetByClinicIdAsync(user.ClinicId, cancellationToken);
            return Result<StaffQuickMessageListDto>.Success(await StaffQuickMessageCatalog.BuildAsync(
                list, user.ClinicId, user.Id, _messages, DateTime.UtcNow, cancellationToken));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffQuickMessageListDto>.Failure("Impossible de charger les messages rapides.", ex);
        }
    }
}

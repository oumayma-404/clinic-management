using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat.Commands;

/// <summary>
/// Replaces the cabinet's quick messages (US-6). <see cref="Version"/> is what the editor opened with — 0 for the
/// defaults — and a colleague's save in between is a 409, never overwritten (AC-6.5).
/// </summary>
public class SaveStaffQuickMessagesCommand : IRequest<Result<StaffQuickMessageListDto>>
{
    public uint Version { get; set; }
    public List<StaffQuickMessageInputDto> Items { get; set; } = new();
}

public class SaveStaffQuickMessagesCommandHandler : IRequestHandler<SaveStaffQuickMessagesCommand, Result<StaffQuickMessageListDto>>
{
    private readonly IStaffQuickMessageListRepository _lists;
    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<SaveStaffQuickMessagesCommandHandler> _logger;

    public SaveStaffQuickMessagesCommandHandler(
        IStaffQuickMessageListRepository lists,
        IStaffMessageRepository messages,
        IUserRepository users,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        ILogger<SaveStaffQuickMessagesCommandHandler> logger)
    {
        _lists = lists;
        _messages = messages;
        _users = users;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<StaffQuickMessageListDto>> Handle(SaveStaffQuickMessagesCommand request, CancellationToken cancellationToken)
    {
        StaffQuickMessageList list;
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

            var inputs = new List<StaffQuickMessageInput>(request.Items.Count);
            foreach (var item in request.Items)
            {
                if (!StaffQuickMessageCatalog.TryParseAudience(item.For, out var audience))
                {
                    return Result<StaffQuickMessageListDto>.Failure(
                        $"Choisissez à qui s'adresse « {item.Label?.Trim()} ».", StaffChatRefusals.InvalidQuickMessagesCode);
                }

                inputs.Add(new StaffQuickMessageInput(item.Key, item.Label ?? string.Empty, audience, item.AsksForPatient, item.Urgent));
            }

            var now = DateTime.UtcNow;
            var existing = await _lists.GetByClinicIdAsync(clinicId, cancellationToken);

            // 0 = the editor opened on the defaults; anything else = on a saved list. Either side moving is a 409.
            if ((existing is null) != (request.Version == 0))
            {
                throw new ConflictException(ErrorMessages.Conflict);
            }

            try
            {
                if (existing is null)
                {
                    list = StaffQuickMessageList.Create(clinicId, inputs, now);
                    await _lists.AddAsync(list, cancellationToken);
                }
                else
                {
                    _unitOfWork.SetExpectedVersion(existing, request.Version);
                    existing.Replace(inputs, now);
                    list = existing;
                }
            }
            catch (ArgumentException ex)
            {
                return Result<StaffQuickMessageListDto>.Failure(ex.Message, StaffChatRefusals.InvalidQuickMessagesCode);
            }

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Two first saves at once: the cabinet's unique index refuses the second.
                throw new ConflictException(ErrorMessages.Conflict, ex);
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffQuickMessageListDto>.Failure("Les messages rapides n'ont pas pu être enregistrés.", ex);
        }

        await StaffQuickMessageSignal.SendAsync(_realtime, clinicId, _logger, cancellationToken);
        return Result<StaffQuickMessageListDto>.Success(await StaffQuickMessageCatalog.BuildAsync(
            list, clinicId, userId, _messages, DateTime.UtcNow, cancellationToken));
    }

    /// <summary>PostgreSQL 23505 — matched on the type name, following <c>UnitOfWork.IsExclusionViolation</c>.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505")
            {
                return true;
            }
        }

        return false;
    }
}

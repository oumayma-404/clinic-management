using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.StaffChat.Queries;

/// <summary>The « Messages » badge: what rings this person, unread, and the newest of it.</summary>
public class GetStaffChatUnreadQuery : IRequest<Result<StaffChatUnreadDto>>
{
}

public class GetStaffChatUnreadQueryHandler : IRequestHandler<GetStaffChatUnreadQuery, Result<StaffChatUnreadDto>>
{
    public const int RecentShown = 5;

    // Read past the silent ones so the shown five are five that ring.
    private const int RecentScanned = 30;

    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;

    public GetStaffChatUnreadQueryHandler(
        IStaffMessageRepository messages,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext)
    {
        _messages = messages;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffChatUnreadDto>> Handle(GetStaffChatUnreadQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<StaffChatUnreadDto>.FailureFrom(loaded);
            }

            var roster = loaded.Value!;
            var viewerId = roster.Viewer.UserId;
            var conversations = StaffChatInbox.Candidates(roster).ToDictionary(c => c.Key, StringComparer.Ordinal);
            var keys = conversations.Keys.ToList();
            bool Rings(string key, string senderId) =>
                conversations.TryGetValue(key, out var c) && c.Alerts(viewerId, senderId, roster.Members);

            var ringing = (await _messages.GetUnreadCountsAsync(
                    roster.ClinicId, viewerId, keys, roster.Viewer.CreatedAt, cancellationToken))
                .Where(u => Rings(u.ConversationKey, u.SenderUserId))
                .ToList();

            var recent = (await _messages.GetRecentUnreadAsync(
                    roster.ClinicId, viewerId, keys, roster.Viewer.CreatedAt, RecentScanned, cancellationToken))
                .Where(m => Rings(m.ConversationKey, m.SenderUserId))
                .Take(RecentShown)
                .Select(m => StaffChatInbox.ToMessageDto(m, roster))
                .ToList();

            return Result<StaffChatUnreadDto>.Success(new StaffChatUnreadDto
            {
                Total = ringing.Sum(u => u.Count),
                HasUrgent = ringing.Any(u => u.UrgentCount > 0),
                Recent = recent,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffChatUnreadDto>.Failure("Impossible de charger les messages non lus.", ex);
        }
    }
}

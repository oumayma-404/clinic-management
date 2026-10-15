using System.Globalization;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.StaffChat.Queries;

/// <summary>The caller's threads: those with history by latest message, then the empty ones A→Z (AC-1.3).</summary>
public class GetStaffConversationsQuery : IRequest<Result<List<StaffConversationDto>>>
{
}

public class GetStaffConversationsQueryHandler : IRequestHandler<GetStaffConversationsQuery, Result<List<StaffConversationDto>>>
{
    private static readonly StringComparer FrenchOrder = StringComparer.Create(new CultureInfo("fr-FR"), ignoreCase: true);

    private readonly IStaffMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IStaffPresence _presence;
    private readonly IPatientRepository _patients;

    public GetStaffConversationsQueryHandler(
        IStaffMessageRepository messages,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IStaffPresence presence,
        IPatientRepository patients)
    {
        _messages = messages;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _presence = presence;
        _patients = patients;
    }

    public async Task<Result<List<StaffConversationDto>>> Handle(GetStaffConversationsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<List<StaffConversationDto>>.FailureFrom(loaded);
            }

            var roster = loaded.Value!;
            var viewerId = roster.Viewer.UserId;
            var candidates = StaffChatInbox.Candidates(roster);
            var keys = candidates.Select(c => c.Key).ToList();

            var latest = (await _messages.GetLatestPerConversationAsync(roster.ClinicId, keys, cancellationToken))
                .ToDictionary(m => m.ConversationKey, StringComparer.Ordinal);
            var unread = (await _messages.GetUnreadCountsAsync(
                    roster.ClinicId, viewerId, keys, roster.Viewer.CreatedAt, cancellationToken))
                .ToLookup(u => u.ConversationKey, StringComparer.Ordinal);
            var lastDtos = latest.ToDictionary(e => e.Key, e => StaffChatInbox.ToMessageDto(e.Value, roster), StringComparer.Ordinal);
            await StaffMessageLinks.FillAsync(
                latest.Values.Select(m => (m, lastDtos[m.ConversationKey])).ToList(), roster, _patients, _messages, cancellationToken);

            var conversations = candidates
                .Where(c => StaffChatInbox.IsOffered(c, roster, latest.ContainsKey(c.Key)))
                .Select(c => new StaffConversationDto
                {
                    Key = c.Key,
                    Kind = StaffChatInbox.KindOf(c),
                    Title = StaffChatInbox.TitleOf(c, roster),
                    Members = c.Members(roster.Members)
                        .Select(id => StaffChatInbox.ToColleagueDto(roster.Find(id)!, roster.ClinicId, _presence))
                        .ToList(),
                    LastMessage = lastDtos.GetValueOrDefault(c.Key),
                    UnreadCount = unread[c.Key].Sum(u => u.Count),
                    HasUrgentUnread = unread[c.Key].Any(u => u.UrgentCount > 0 && c.Alerts(viewerId, u.SenderUserId, roster.Members)),
                    ReadOnly = StaffChatInbox.IsReadOnly(c, roster),
                })
                .OrderBy(c => c.LastMessage is null ? 1 : 0)
                .ThenByDescending(c => c.LastMessage?.SentAt)
                .ThenBy(c => c.LastMessage is null ? QuietRank(c.Kind) : 0)
                .ThenBy(c => c.Title, FrenchOrder)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .ToList();

            return Result<List<StaffConversationDto>>.Success(conversations);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<StaffConversationDto>>.Failure("Impossible de charger les conversations.", ex);
        }
    }

    /// <summary>Among threads nobody has written in yet, the shared ones come before the colleagues (AC-1.3).</summary>
    private static int QuietRank(string kind) => kind switch
    {
        "clinic" => 1,
        "desk" => 2,
        _ => 3,
    };
}

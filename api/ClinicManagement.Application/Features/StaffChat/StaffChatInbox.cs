using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>Which threads a person has, how each is titled, and whether it can still be written to — one owner for every read and the send.</summary>
public static class StaffChatInbox
{
    /// <summary>The cabinet thread is offered from three accounts up; with two it would duplicate the two-person one (FR-1).</summary>
    public const int ClinicThreadMinimumAccounts = 3;

    /// <summary>Every thread the viewer is a member of, whether or not anything was written there yet.</summary>
    public static IReadOnlyList<StaffConversation> Candidates(StaffChatRoster roster)
    {
        var viewer = roster.Viewer;
        var candidates = new List<StaffConversation> { StaffConversation.Clinic };

        if (viewer.IsSecretary)
        {
            candidates.AddRange(roster.Colleagues.Where(c => !c.IsSecretary).Select(c => StaffConversation.Desk(c.UserId)));
        }
        else
        {
            candidates.Add(StaffConversation.Desk(viewer.UserId));
        }

        candidates.AddRange(roster.Colleagues
            .Where(c => c.UserId != viewer.UserId)
            .Select(c => StaffConversation.Direct(viewer.UserId, c.UserId)));

        return candidates.Where(c => c.IsMember(viewer.UserId, roster.Members)).ToList();
    }

    /// <summary>Whether an empty thread is offered; a thread with history is always shown, even read-only.</summary>
    public static bool IsOffered(StaffConversation conversation, StaffChatRoster roster, bool hasMessages) =>
        hasMessages || conversation.Kind switch
        {
            StaffConversationKind.Clinic => roster.Colleagues.Count(c => c.IsActive) >= ClinicThreadMinimumAccounts,
            _ => !IsReadOnly(conversation, roster),
        };

    /// <summary>The other side is gone: a deactivated peer, a deactivated desk owner, or a desk with no active secretary (FR-9).</summary>
    public static bool IsReadOnly(StaffConversation conversation, StaffChatRoster roster) => conversation.Kind switch
    {
        StaffConversationKind.Direct => roster.Find(Other(conversation, roster.Viewer.UserId))?.IsActive != true,
        StaffConversationKind.Desk => roster.Find(conversation.First!)?.IsActive != true
            || !roster.Colleagues.Any(c => c.IsSecretary && c.IsActive),
        _ => false,
    };

    /// <summary>A desk thread is « Secrétariat » to its owner and « Secrétariat · owner » to the desk, so it never reads like the two-person thread with that owner (FR-1).</summary>
    public static string TitleOf(StaffConversation conversation, StaffChatRoster roster) => conversation.Kind switch
    {
        StaffConversationKind.Clinic => "Tout le cabinet",
        StaffConversationKind.Desk when conversation.First == roster.Viewer.UserId => "Secrétariat",
        StaffConversationKind.Desk => $"Secrétariat · {roster.NameOf(conversation.First!)}",
        _ => roster.NameOf(Other(conversation, roster.Viewer.UserId)),
    };

    public static string KindOf(StaffConversation conversation) => conversation.Kind switch
    {
        StaffConversationKind.Direct => "direct",
        StaffConversationKind.Desk => "desk",
        _ => "clinic",
    };

    public static StaffColleagueDto ToColleagueDto(StaffChatColleague colleague, Guid clinicId, IStaffPresence presence) => new()
    {
        UserId = colleague.UserId,
        Name = colleague.Name,
        IsDentist = colleague.IsDentist,
        IsSecretary = colleague.IsSecretary,
        Online = colleague.IsActive && presence.IsOnline(clinicId, colleague.UserId),
        Active = colleague.IsActive,
    };

    public static StaffMessageDto ToMessageDto(StaffMessage message, StaffChatRoster roster) => new()
    {
        Id = message.Id,
        Conversation = message.ConversationKey,
        Sender = new StaffMessageSenderDto { UserId = message.SenderUserId, Name = roster.NameOf(message.SenderUserId) },
        SentAt = message.SentAt,
        Body = message.Body,
        QuickMessage = message.QuickMessageKey is null
            ? null
            : new StaffQuickMessageRefDto
            {
                Key = message.QuickMessageKey,
                Label = message.QuickMessageLabel ?? string.Empty,
                Urgent = message.IsUrgent,
            },
        ClaimedBy = message.ClaimedByUserId is null || message.ClaimedAt is null
            ? null
            : new StaffMessageClaimDto
            {
                UserId = message.ClaimedByUserId,
                Name = roster.NameOf(message.ClaimedByUserId),
                At = message.ClaimedAt.Value,
            },
    };

    /// <summary>As above, plus « Vu par »: the thread's other members whose read marker reaches this message (AC-5.4).</summary>
    public static StaffMessageDto ToMessageDto(
        StaffMessage message, StaffChatRoster roster, StaffConversation conversation,
        IReadOnlyCollection<StaffChatReadMarker> markers)
    {
        var dto = ToMessageDto(message, roster);
        var members = conversation.Members(roster.Members).ToHashSet(StringComparer.Ordinal);
        dto.SeenBy = markers
            .Where(r => r.UserId != message.SenderUserId && members.Contains(r.UserId) && r.LastReadAt >= message.SentAt)
            .OrderBy(r => r.LastReadAt)
            .ThenBy(r => r.UserId, StringComparer.Ordinal)
            .Select(r => new StaffMessageSenderDto { UserId = r.UserId, Name = roster.NameOf(r.UserId) })
            .ToList();
        return dto;
    }

    /// <summary>The thread the viewer asked for, if it is a real key and they belong to it — otherwise nothing, never another cabinet's thread.</summary>
    public static StaffConversation? Resolve(string? key, StaffChatRoster roster) =>
        StaffConversation.TryParse(key, out var conversation) && conversation.IsMember(roster.Viewer.UserId, roster.Members)
            ? conversation
            : null;

    private static string Other(StaffConversation direct, string viewerUserId) =>
        direct.First == viewerUserId ? direct.Second! : direct.First!;
}

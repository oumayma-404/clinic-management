using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>Who a staff chat thread is between and who it rings (staff-chat FR-1, FR-2) — a derived key, never a row.</summary>
/// <remarks>Ids are URI-escaped inside the key because user ids contain <c>|</c> (<c>local|{guid}</c>).</remarks>
public sealed class StaffConversation : ValueObject
{
    private const string DirectPrefix = "dm";
    private const string DeskPrefix = "desk";
    private const string ClinicKey = "clinic";
    private const char Separator = ':';

    public static StaffConversation Clinic { get; } = new(StaffConversationKind.Clinic, null, null);

    public StaffConversationKind Kind { get; }

    /// <summary>Direct: the ordinally smaller member. Desk: the owner. Clinic: null.</summary>
    public string? First { get; }

    /// <summary>Direct: the ordinally larger member. Otherwise null.</summary>
    public string? Second { get; }

    public string Key { get; }

    private StaffConversation(StaffConversationKind kind, string? first, string? second)
    {
        Kind = kind;
        First = first;
        Second = second;
        Key = kind switch
        {
            StaffConversationKind.Direct => $"{DirectPrefix}{Separator}{Uri.EscapeDataString(first!)}{Separator}{Uri.EscapeDataString(second!)}",
            StaffConversationKind.Desk => $"{DeskPrefix}{Separator}{Uri.EscapeDataString(first!)}",
            _ => ClinicKey,
        };
    }

    public static StaffConversation Direct(string userA, string userB)
    {
        if (string.IsNullOrWhiteSpace(userA) || string.IsNullOrWhiteSpace(userB))
        {
            throw new ArgumentException("Une conversation à deux nécessite deux personnes.");
        }

        if (string.Equals(userA, userB, StringComparison.Ordinal))
        {
            throw new ArgumentException("On ne peut pas s'écrire à soi-même.");
        }

        return string.CompareOrdinal(userA, userB) < 0
            ? new StaffConversation(StaffConversationKind.Direct, userA, userB)
            : new StaffConversation(StaffConversationKind.Direct, userB, userA);
    }

    public static StaffConversation Desk(string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Le secrétariat d'une conversation doit avoir un titulaire.", nameof(ownerUserId));
        }

        return new StaffConversation(StaffConversationKind.Desk, ownerUserId, null);
    }

    /// <summary>Reads a key back; false for anything this class could not have produced.</summary>
    public static bool TryParse(string? key, out StaffConversation conversation)
    {
        conversation = Clinic;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key == ClinicKey)
        {
            return true;
        }

        var parts = key.Split(Separator);
        try
        {
            if (parts.Length == 3 && parts[0] == DirectPrefix)
            {
                var a = Uri.UnescapeDataString(parts[1]);
                var b = Uri.UnescapeDataString(parts[2]);
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || string.CompareOrdinal(a, b) >= 0)
                {
                    return false;
                }

                conversation = new StaffConversation(StaffConversationKind.Direct, a, b);
                return conversation.Key == key;
            }

            if (parts.Length == 2 && parts[0] == DeskPrefix)
            {
                var owner = Uri.UnescapeDataString(parts[1]);
                if (string.IsNullOrWhiteSpace(owner))
                {
                    return false;
                }

                conversation = new StaffConversation(StaffConversationKind.Desk, owner, null);
                return conversation.Key == key;
            }
        }
        catch (UriFormatException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Whether <paramref name="userId"/> reads this thread; a desk thread needs a non-secretary owner and a secretary (EC-2, EC-14).</summary>
    public bool IsMember(string userId, IReadOnlyCollection<StaffChatMember> roster)
    {
        var user = Find(roster, userId);
        if (user is null)
        {
            return false;
        }

        return Kind switch
        {
            // Both people must belong to this cabinet, or a key naming an outsider would answer as a real thread.
            StaffConversationKind.Direct => (userId == First || userId == Second)
                && Find(roster, First) is not null && Find(roster, Second) is not null,
            StaffConversationKind.Desk => DeskIsOpen(roster) && (userId == First || user.IsSecretary),
            _ => true,
        };
    }

    /// <summary>Every account that reads this thread, active or not — the tabs a new message must refresh.</summary>
    public IReadOnlyList<string> Members(IReadOnlyCollection<StaffChatMember> roster) =>
        roster.Where(m => IsMember(m.UserId, roster)).Select(m => m.UserId).ToList();

    /// <summary>The FR-2 table: does a message from the sender ring the viewer? A member it does not ring sees it unread, silently (AC-5.3).</summary>
    public bool Alerts(string viewerUserId, string senderUserId, IReadOnlyCollection<StaffChatMember> roster)
    {
        if (viewerUserId == senderUserId || !IsMember(viewerUserId, roster) || Find(roster, viewerUserId)?.IsActive != true)
        {
            return false;
        }

        if (Kind != StaffConversationKind.Desk)
        {
            return true;
        }

        return senderUserId == First || viewerUserId == First;
    }

    /// <summary>
    /// FR-6: « Je m'en occupe » is offered on a message the owner sent to their secretariat, to the active secretaries
    /// of that thread — never to the owner, never on a secretary's own reply.
    /// </summary>
    public bool OffersClaim(string senderUserId, string takerUserId, IReadOnlyCollection<StaffChatMember> roster)
    {
        if (Kind != StaffConversationKind.Desk || senderUserId != First || takerUserId == First)
        {
            return false;
        }

        var taker = Find(roster, takerUserId);
        return taker is { IsSecretary: true, IsActive: true } && IsMember(takerUserId, roster);
    }

    /// <summary>The accounts a message from <paramref name="senderUserId"/> rings.</summary>
    public IReadOnlyList<string> AlertsFor(string senderUserId, IReadOnlyCollection<StaffChatMember> roster) =>
        roster.Where(m => Alerts(m.UserId, senderUserId, roster)).Select(m => m.UserId).ToList();

    private bool DeskIsOpen(IReadOnlyCollection<StaffChatMember> roster)
    {
        var owner = Find(roster, First);
        return owner is { IsSecretary: false } && roster.Any(m => m.IsSecretary);
    }

    private static StaffChatMember? Find(IReadOnlyCollection<StaffChatMember> roster, string? userId) =>
        userId is null ? null : roster.FirstOrDefault(m => m.UserId == userId);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Key;
    }

    public override string ToString() => Key;
}

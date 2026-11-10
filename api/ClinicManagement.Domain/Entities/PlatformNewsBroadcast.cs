using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

/// <summary>One cleaned row: its line and, optionally, its guide (a description and numbered steps).</summary>
/// <remarks>⚠️ A copy must pass all four — a re-listed copy that stops at <c>Text</c> silently drops the guide.</remarks>
public sealed record PlatformNewsItemInput(
    string Emoji, string Text, string? Description = null, IReadOnlyList<string>? Steps = null);

/// <summary>
/// One news e-mail the vendor sent from the console, and every address it went to. No ClinicId: it belongs to the
/// vendor, and its recipients are fixed at the click so the confirm's counts are the counts attempted.
/// </summary>
public class PlatformNewsBroadcast : AggregateRoot<Guid>
{
    public const int SubjectMaxLength = 120;
    public const int MaxItems = 12;
    public const int SentByNameMaxLength = 200;

    private readonly List<PlatformNewsItem> _items = new();
    private readonly List<PlatformNewsRecipient> _recipients = new();

    /// <summary>The console's key for one press of « Envoyer » — unique, so a double-click is one broadcast.</summary>
    public Guid RequestId { get; private set; }

    public string Subject { get; private set; } = string.Empty;
    public IReadOnlyCollection<PlatformNewsItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<PlatformNewsRecipient> Recipients => _recipients.AsReadOnly();
    public Guid SentByAccountId { get; private set; }
    public string SentByName { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Set by the console's « Masquer dans l'app »: the e-mail is gone, the page in the app stops showing it.</summary>
    public DateTime? HiddenFromAppAtUtc { get; private set; }

    public bool IsVisibleInApp => HiddenFromAppAtUtc is null;

    private PlatformNewsBroadcast() { } // For EF Core

    public static PlatformNewsBroadcast Create(
        Guid requestId,
        string subject,
        IEnumerable<PlatformNewsItemInput> items,
        IEnumerable<PlatformNewsRecipientInput> recipients,
        Guid sentByAccountId,
        string sentByName,
        DateTime nowUtc)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("La clé d'envoi est obligatoire.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectMaxLength)
        {
            throw new ArgumentException(
                $"L'objet est obligatoire et fait {SubjectMaxLength} caractères au plus.", nameof(subject));
        }

        var itemList = items.ToList();
        if (itemList.Count == 0 || itemList.Count > MaxItems)
        {
            throw new ArgumentException($"Entre 1 et {MaxItems} nouveautés.", nameof(items));
        }

        var broadcast = new PlatformNewsBroadcast
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            Subject = subject,
            SentByAccountId = sentByAccountId,
            SentByName = sentByName.Length > SentByNameMaxLength ? sentByName[..SentByNameMaxLength] : sentByName,
            CreatedAtUtc = nowUtc,
        };

        for (var i = 0; i < itemList.Count; i++)
        {
            var item = itemList[i];
            broadcast._items.Add(new PlatformNewsItem(item.Emoji, item.Text, i, item.Description, item.Steps));
        }

        foreach (var recipient in recipients)
        {
            broadcast._recipients.Add(new PlatformNewsRecipient(broadcast.Id, recipient));
        }

        if (broadcast._recipients.Count == 0)
        {
            throw new ArgumentException("Aucun destinataire.", nameof(recipients));
        }

        return broadcast;
    }

    public IReadOnlyList<PlatformNewsItem> OrderedItems() => _items.OrderBy(i => i.SortOrder).ToList();

    public IReadOnlyList<PlatformNewsRecipient> PendingRecipients() =>
        _recipients.Where(r => r.Status == PlatformNewsDeliveryStatus.Pending).ToList();

    /// <summary>Every recipient has a final outcome — nothing pending, nothing claimed.</summary>
    public bool IsSettled => _recipients.All(r =>
        r.Status is PlatformNewsDeliveryStatus.Sent or PlatformNewsDeliveryStatus.Failed);

    /// <summary>Takes one pending recipient for the current run; saved before the e-mail leaves.</summary>
    public void Claim(Guid recipientId, DateTime nowUtc) => Recipient(recipientId).MarkSending(nowUtc);

    public void RecordSent(Guid recipientId, DateTime nowUtc) => Recipient(recipientId).MarkSent(nowUtc);

    public void RecordFailed(Guid recipientId, string error, DateTime nowUtc) =>
        Recipient(recipientId).MarkFailed(error, nowUtc);

    /// <summary>A claim older than <paramref name="claimedBefore"/> belongs to a run that died: failed, never re-sent.</summary>
    public int ExpireStaleClaims(DateTime claimedBefore, DateTime nowUtc)
    {
        var stale = _recipients
            .Where(r => r.Status == PlatformNewsDeliveryStatus.Sending && r.AttemptedAtUtc < claimedBefore)
            .ToList();

        foreach (var recipient in stale)
        {
            recipient.MarkFailed("Envoi interrompu avant confirmation : non renvoyé pour éviter un doublon.", nowUtc);
        }

        return stale.Count;
    }

    public void Complete(DateTime nowUtc)
    {
        if (!IsSettled)
        {
            throw new InvalidOperationException("Des destinataires n'ont pas encore été traités.");
        }

        CompletedAtUtc ??= nowUtc;
    }

    /// <summary>Takes the news off the cabinets' « Nouveautés » page; returns false when it was already hidden.</summary>
    public bool HideFromApp(DateTime nowUtc)
    {
        if (HiddenFromAppAtUtc is not null)
        {
            return false;
        }

        HiddenFromAppAtUtc = nowUtc;
        return true;
    }

    /// <summary>Puts a hidden news back on the page; returns false when it was already shown.</summary>
    public bool ShowInApp()
    {
        if (HiddenFromAppAtUtc is null)
        {
            return false;
        }

        HiddenFromAppAtUtc = null;
        return true;
    }

    private PlatformNewsRecipient Recipient(Guid recipientId) =>
        _recipients.FirstOrDefault(r => r.Id == recipientId)
        ?? throw new InvalidOperationException("Destinataire inconnu pour cet envoi.");
}

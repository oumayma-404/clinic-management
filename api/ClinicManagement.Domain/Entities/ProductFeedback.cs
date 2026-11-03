using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// What one person of a cabinet told the vendor about APEXA (<c>guided-first-days</c> E): the week-one question
/// or « Donner mon avis ou une idée » at any time. E-mailed to the console team when written and listed in the
/// console's « Retours ».
///
/// <para>⚠️ <b>An <c>Entity</c>, deliberately not an aggregate root</b> — <c>StaffMessage</c>'s reasoning: the
/// audit interceptor records one row per mutated <i>aggregate</i>, and a message to the vendor is neither the
/// practice's clinical or financial history nor work worth a journal line.</para>
///
/// <para>⚠️ It is a message <b>written by a clinic user to the vendor</b>, never copied from a record; the form
/// asks for no patient name, and the vendor console may show it (owner's answer, 2026-11-03). Kind, source and
/// areas are validated strings (<c>FeedbackRules</c>), so the wire never carries a raw enum.</para>
/// </summary>
public class ProductFeedback : Entity<Guid>
{
    public const int MaxMessageLength = 2000;
    public const int MaxScreenLength = 120;

    public Guid ClinicId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string AuthorRole { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public int? HelpRating { get; private set; }
    public int? EaseRating { get; private set; }
    public string? LikedAreasCsv { get; private set; }
    public string? HardAreasCsv { get; private set; }
    public string? Message { get; private set; }
    public string? FromScreen { get; private set; }
    public bool MayContact { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ProductFeedback() { } // For EF Core

    public ProductFeedback(
        Guid clinicId,
        string userId,
        string authorRole,
        string kind,
        string source,
        int? helpRating,
        int? easeRating,
        IReadOnlyCollection<string> likedAreas,
        IReadOnlyCollection<string> hardAreas,
        string? message,
        string? fromScreen,
        bool mayContact,
        DateTime? nowUtc = null)
    {
        if (clinicId == Guid.Empty) throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("L'auteur est requis.", nameof(userId));
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Le type est requis.", nameof(kind));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("La source est requise.", nameof(source));
        ValidateRating(helpRating, nameof(helpRating));
        ValidateRating(easeRating, nameof(easeRating));

        var text = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (text is { Length: > MaxMessageLength }) text = text[..MaxMessageLength];

        if (helpRating is null && easeRating is null && likedAreas.Count == 0 && hardAreas.Count == 0 && text is null)
        {
            throw new ArgumentException("Un avis vide n'est pas enregistré.");
        }

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        UserId = userId;
        AuthorRole = authorRole;
        Kind = kind;
        Source = source;
        HelpRating = helpRating;
        EaseRating = easeRating;
        LikedAreasCsv = likedAreas.Count == 0 ? null : string.Join(',', likedAreas);
        HardAreasCsv = hardAreas.Count == 0 ? null : string.Join(',', hardAreas);
        Message = text;
        var screen = string.IsNullOrWhiteSpace(fromScreen) ? null : fromScreen.Trim();
        FromScreen = screen is { Length: > MaxScreenLength } ? screen[..MaxScreenLength] : screen;
        MayContact = mayContact;
        CreatedAt = nowUtc ?? DateTime.UtcNow;
    }

    public IReadOnlyList<string> LikedAreas() => Split(LikedAreasCsv);

    public IReadOnlyList<string> HardAreas() => Split(HardAreasCsv);

    private static IReadOnlyList<string> Split(string? csv) =>
        string.IsNullOrEmpty(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static void ValidateRating(int? rating, string name)
    {
        if (rating is < 1 or > 5) throw new ArgumentOutOfRangeException(name, rating, "Une note va de 1 à 5.");
    }
}

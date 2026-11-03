using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// Whether this person is still asked the week-one question (<c>guided-first-days</c> E), keyed by the user's own id
/// like <c>UserDashboardPreference</c>. Absent row = never shown yet.
///
/// <para>The question is asked at most <see cref="MaxShows"/> times: shown, « Plus tard » once (a week), shown again,
/// and then never — an answer or « Ne plus demander » closes it at once. A question that comes back every week is
/// harassment, which the owner ruled out in as many words.</para>
/// </summary>
public class UserFeedbackPrompt : Entity<string>
{
    public const int MaxShows = 2;
    public static readonly TimeSpan LaterDelay = TimeSpan.FromDays(7);

    public const string ClosedByAnswer = "answered";
    public const string ClosedByNever = "never";

    public int TimesShown { get; private set; }
    public DateTime? LaterUntil { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? ClosedReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private UserFeedbackPrompt() { } // For EF Core

    public UserFeedbackPrompt(string userId, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("L'identifiant de l'utilisateur est requis.", nameof(userId));
        }

        Id = userId;
        CreatedAt = nowUtc ?? DateTime.UtcNow;
    }

    public bool IsClosed => ClosedAt is not null;

    /// <summary>Whether the question may be put now — the prompt's half of the rule; the account's age and the cabinet's use are the query's.</summary>
    public bool MayShow(DateTime nowUtc) =>
        !IsClosed && TimesShown < MaxShows && (LaterUntil is null || LaterUntil <= nowUtc);

    /// <summary>
    /// The card appeared. Idempotent within a showing: the browser reports it on every page that mounts the card,
    /// so the count is « which showing is this » — the first, or the one after « Plus tard » — never a tally of pages.
    /// </summary>
    public void MarkShown(DateTime? nowUtc = null)
    {
        var showing = LaterUntil is null ? 1 : MaxShows;
        if (TimesShown < showing) TimesShown = showing;
        UpdatedAt = nowUtc ?? DateTime.UtcNow;
    }

    /// <summary>« Plus tard »: a week. Past the last showing it closes instead, so « later » can never mean « again ».</summary>
    public void Later(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (TimesShown >= MaxShows)
        {
            Close(ClosedByNever, now);
            return;
        }

        LaterUntil = now + LaterDelay;
        UpdatedAt = now;
    }

    public void Close(string reason, DateTime? nowUtc = null)
    {
        if (IsClosed) return;
        var now = nowUtc ?? DateTime.UtcNow;
        ClosedAt = now;
        ClosedReason = reason;
        UpdatedAt = now;
    }
}

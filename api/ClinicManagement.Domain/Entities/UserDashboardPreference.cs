using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One user's dashboard layout choices — today, which KPI cards they have hidden.
/// <para>
/// 1:1 with <see cref="User"/>: the entity <see cref="Common.Entity{TId}.Id"/> <b>is</b> the owning user's id
/// (shared primary key), the same shape <see cref="ClinicReminderSettings"/> uses against
/// <see cref="Clinic"/>. A row is created lazily on the first save — a user who has never customised anything
/// has no row, and the read path treats "no row" and "nothing hidden" as the same answer.
/// </para>
/// <para>
/// <b>No <c>ClinicId</c></b>, deliberately, following <see cref="NotificationRead"/>: a user belongs to exactly
/// one clinic, so the user id already scopes the row and there is nothing for the EF global clinic filter to
/// add. Adding the column would create a second, independently-writable answer to "whose clinic is this?" that
/// could disagree with <c>User.ClinicId</c>.
/// </para>
/// <para>
/// <b>Hidden, not visible, is what gets stored.</b> Storing the visible set would mean every KPI added in a
/// later release starts life invisible to every existing user until they go and switch it on — a new figure
/// nobody asked to hide, hidden. Storing the hidden set makes the default "show it", so the dashboard can grow
/// and a user's stored choices keep meaning exactly what they meant when they made them.
/// </para>
/// </summary>
public class UserDashboardPreference : Entity<string>
{
    /// <summary>
    /// Upper bound on how many keys one user may hide. Not a business rule — a write bound, so a crafted
    /// request cannot grow this row without limit. Comfortably above the number of KPIs the dashboard has.
    /// </summary>
    public const int MaxHiddenKeys = 64;

    /// <summary>Upper bound on a single key's length, for the same reason.</summary>
    public const int MaxKeyLength = 64;

    private const char Separator = ',';

    /// <summary>
    /// The hidden KPI keys as a canonical CSV (ordered, de-duplicated), or empty when nothing is hidden.
    /// Mirrors how <see cref="ClinicReminderSettings.LeadTimeHours"/> stores its tiers: a short, opaque,
    /// order-insensitive set is cheaper to read and diff as one column than as a child table.
    /// <para>
    /// The keys themselves are <b>opaque here</b>. Which KPIs exist is a presentation concern that changes with
    /// the dashboard, and the Domain has no business holding that list — the Application command validates
    /// against the known set before calling <see cref="SetHiddenKpis"/>, so an unknown key is refused at the
    /// edge rather than accumulating quietly in this column.
    /// </para>
    /// </summary>
    public string HiddenKpisCsv { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private UserDashboardPreference() { } // For EF Core

    public UserDashboardPreference(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("L'identifiant de l'utilisateur est requis.", nameof(userId));
        }

        Id = userId;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>The hidden keys, parsed. Never null; empty when the user hides nothing.</summary>
    public IReadOnlyList<string> HiddenKpis() => Parse(HiddenKpisCsv);

    /// <summary>
    /// Replaces the hidden set wholesale. <b>Replace, not merge</b> — the caller is a settings panel that always
    /// knows the full intended state, and a merge could never express "show this one again".
    /// </summary>
    public void SetHiddenKpis(IEnumerable<string>? keys)
    {
        HiddenKpisCsv = Format(keys);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Normalizes a set of keys to the stored CSV: trimmed, blanks dropped, de-duplicated case-insensitively,
    /// over-long keys rejected, and capped at <see cref="MaxHiddenKeys"/>. Sorted so two equivalent sets produce
    /// the same string — otherwise re-saving the same choices in a different order would look like a change.
    /// </summary>
    public static string Format(IEnumerable<string>? keys)
    {
        if (keys is null)
        {
            return string.Empty;
        }

        var normalized = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Where(k => k.Length <= MaxKeyLength && !k.Contains(Separator))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Take(MaxHiddenKeys)
            .ToArray();

        return string.Join(Separator, normalized);
    }

    /// <summary>
    /// Parses the stored CSV back to keys. Tolerant on purpose: a key that no longer exists (a KPI removed in a
    /// later release) is returned as-is rather than throwing, and the read side simply finds nothing to hide by
    /// that name. A stored preference must never be able to break the dashboard that reads it.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

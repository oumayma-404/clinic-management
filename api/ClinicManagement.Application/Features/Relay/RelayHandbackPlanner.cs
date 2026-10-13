using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// The tables the cloud keeps writing for a cabinet during a cut (FR-11) — what the return does with each. Together they
/// are exactly the fenced cloud's allowed tables (<c>RelayFence.AllowedOnFencedCloud</c>); a guard holds the two equal.
/// </summary>
public static class RelayHandbackRules
{
    /// <summary>Never taken from the PC: accounts and the subscription are the cloud's (FR-11) — the sign-in traces travel apart.</summary>
    public static readonly IReadOnlySet<string> NeverReturned = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(User), nameof(UserRecoveryCode), nameof(ClinicSubscription), nameof(SubscriptionPeriod),
    };

    /// <summary>Taken from the PC unless the cloud changed the same row meanwhile — then the cloud's stays, and both are listed.</summary>
    public static readonly IReadOnlySet<string> CloudKeepsIfChanged = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Doctor), nameof(StaffNotification),
    };

    /// <summary>Applied, but never worth a line of « Modifications à vérifier »: delivery state and per-person marks.</summary>
    public static readonly IReadOnlySet<string> NotReviewed = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Notification), nameof(NotificationRead), nameof(NotificationDismissal), nameof(UserDashboardPreference),
    };

    /// <summary>The cloud's own FR-11 writes during the cut are not « changes made before the cut ».</summary>
    public static bool IsCloudOwned(string table) => NeverReturned.Contains(table) || CloudKeepsIfChanged.Contains(table);

    /// <summary>
    /// Rows other records are filed under, never records whose parts must stay together: the cloud keeping a patient's
    /// own row (its phone number changed there) does not hold back the fiches, files and histories filed under it.
    /// </summary>
    public static readonly IReadOnlySet<string> Folders = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Patient), nameof(Clinic),
    };
}

/// <summary>A line the return lists (AC-5.6): the cabinet's key, and for a duplicate the cloud's own record of the same save.</summary>
public sealed record RelayPlannedReview(RelayReviewKind Kind, RelayRowKey Key, RelayRowKey? CloudKey, DateTime? CloudChangedAtUtc);

/// <summary>
/// What the return does: the rows it applies, the duplicates it drops (D17), the lines it lists (AC-5.6), and the rows
/// the cabinet changed that the cloud KEEPS because the cloud changed them too (<see cref="Kept"/>).
/// </summary>
public sealed record RelayHandbackPlan(
    IReadOnlyList<RelayRow> Apply,
    IReadOnlyList<RelayRowKey> Dropped,
    IReadOnlyList<RelayPlannedReview> Review,
    IReadOnlyList<RelayRowKey>? Kept = null);

/// <summary>
/// The return's decisions (<c>clinic-pc-copy</c> D17, D18, EC-6, EC-7, FR-11), pure so each one is tested on its own.
///
/// <list type="bullet">
///   <item><b>The cloud's version is never replaced</b> (owner, 2026-10-09 — it reverses EC-7's « the cabinet's version
///   wins »): a row the cloud changed after the PC's base stays the cloud's, a deletion on the PC included; the cabinet's
///   version is listed beside it in « À vérifier » for somebody to apply. Cloud work is never destroyed by a PC.</item>
///   <item><b>FR-11</b>: accounts and the subscription are never taken from the PC; a practitioner record or a bell row
///   the cloud changed during the cut stays the cloud's.</item>
///   <item><b>D17</b>: a save the cloud already recorded (same <c>Idempotency-Key</c>) and that was pressed again on the
///   PC is dropped — only when no other change of the cut points to its rows; otherwise both are kept and listed as a
///   probable duplicate. Never dropped blind (a payment taken on a reprinted note), never applied twice.</item>
///   <item>A change the cloud made that the PC never received and did not touch is <b>kept and listed</b> (AC-5.6).</item>
/// </list>
///
/// <para>⚠️ The cloud's changes written by an earlier return (<see cref="RelayCloudChange.FromRelay"/>) are the cabinet's
/// own rows coming back — never a conflict, never a duplicate. That is what makes a second attempt, after a lost answer,
/// harmless.</para>
///
/// <para>⚠️ <b>Since the takeover is manual the cloud stays writable during a cut</b>, so « changed on both sides » is the
/// ordinary case, and three rules keep it from mangling a record (review findings 4 and 5, 2026-10-13):</para>
/// <list type="bullet">
///   <item><b>A background job's change never counts</b> (<see cref="RelayCloudChange.Automatic"/>): the cloud's agenda
///   job moves every visit of the cut, and counting that kept the cloud's version of every visit the cabinet cancelled,
///   moved or closed.</item>
///   <item><b>Per record, not per row</b>: a row whose record the cloud keeps is kept with it — an act of a fiche the
///   cloud kept, a payment on an échéance it kept — and listed, never applied beside the cloud's version (acts doubled, a
///   payment in la caisse and not on the devis). A patient is a folder, not a record (<see cref="RelayHandbackRules.Folders"/>):
///   a phone number changed in the cloud never holds back the fiches filed under it.</item>
///   <item><b>A deletion on the PC never takes cloud work with it</b>: a row the cloud wrote during the cut that points to
///   it keeps it alive (no <c>ON DELETE CASCADE</c> into the cloud's work).</item>
/// </list>
/// </summary>
public static class RelayHandbackPlanner
{
    /// <param name="owner">The row a row belongs to (an act's fiche); null keeps the decisions per row (older callers).</param>
    /// <param name="cloudRows">
    /// The cloud's current rows for the cloud's changed keys and the PC's deletions — what the PC deleted is judged by its
    /// cloud version (its record, and what still points to it).
    /// </param>
    public static RelayHandbackPlan Plan(
        RelayHandbackRequest request,
        IReadOnlyList<RelayCloudChange> cloudChanges,
        Func<string, JsonElement, IEnumerable<RelayRowKey>> references,
        Func<string, JsonElement, RelayRowKey?>? owner = null,
        IReadOnlyDictionary<RelayRowKey, JsonElement>? cloudRows = null)
    {
        var rows = new Dictionary<RelayRowKey, RelayRow>();
        foreach (var row in request.Rows)
        {
            rows[new RelayRowKey(row.Table, row.Key)] = row;
        }

        // What the cloud wrote during the cut, by a person (the conflict set) or by anybody (what a deletion must spare).
        var cloudWritten = cloudChanges.Where(c => !c.FromRelay).ToList();
        var cloud = cloudWritten.Where(c => !c.Automatic).ToList();
        var cloudByKey = cloud
            .GroupBy(c => new RelayRowKey(c.Table, c.Key))
            .ToDictionary(g => g.Key, g => g.MaxBy(c => c.Seq)!);
        var cloudBySave = cloud
            .Where(c => c.IdempotencyKey is not null)
            .GroupBy(c => c.IdempotencyKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => new RelayRowKey(c.Table, c.Key)).Distinct().ToList(), StringComparer.Ordinal);
        var pcBySave = request.Changes
            .Where(c => c.IdempotencyKey is not null)
            .GroupBy(c => c.IdempotencyKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => new RelayRowKey(c.Table, c.Key)).ToHashSet(), StringComparer.Ordinal);

        IEnumerable<RelayRowKey> PointsTo(RelayRowKey key) =>
            rows.TryGetValue(key, out var r) && r.Row is { } json ? references(r.Table, json) : Enumerable.Empty<RelayRowKey>();

        var dropped = new HashSet<RelayRowKey>();
        var review = new List<RelayPlannedReview>();
        var pairedCloudKeys = new HashSet<RelayRowKey>();

        // D17 — a save recorded on both sides.
        foreach (var (save, pcKeys) in pcBySave.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!cloudBySave.TryGetValue(save, out var cloudKeys))
            {
                continue;
            }

            var group = pcKeys.Where(k => rows.TryGetValue(k, out var r) && r.Row is not null).ToHashSet();
            if (group.Count == 0)
            {
                continue;
            }

            // The cloud's record of the same save is the one the cabinet meant: never listed as a change of its own.
            pairedCloudKeys.UnionWith(cloudKeys);

            var pointedTo = rows.Keys
                .Where(k => !group.Contains(k) && !dropped.Contains(k))
                .Any(k => PointsTo(k).Any(group.Contains));
            if (!pointedTo)
            {
                dropped.UnionWith(group);
                continue;
            }

            // Listed once per record, not per line of it: the group's roots — the rows that point to nothing else of the save.
            var roots = group.Where(k => !PointsTo(k).Any(group.Contains))
                .OrderBy(k => k.Table, StringComparer.Ordinal).ThenBy(k => k.Key, StringComparer.Ordinal);
            foreach (var root in roots)
            {
                RelayRowKey? twin = cloudKeys.Any(c => c.Table == root.Table) ? cloudKeys.First(c => c.Table == root.Table) : null;

                review.Add(new RelayPlannedReview(RelayReviewKind.ProbableDuplicate, root, twin,
                    twin is { } paired && cloudByKey.TryGetValue(paired, out var at) ? at.RecordedAtUtc : null));
            }
        }

        // The row each row belongs to — read off the PC's version, or the cloud's for a row the PC deleted.
        RelayRowKey? OwnerOf(RelayRowKey key)
        {
            if (owner is null)
            {
                return null;
            }

            var json = rows.TryGetValue(key, out var r) && r.Row is { } pc ? pc
                : cloudRows is not null && cloudRows.TryGetValue(key, out var cloudJson) ? cloudJson
                : (JsonElement?)null;
            return json is { } found ? owner(key.Table, found) : null;
        }

        // Every key a row written in the cloud during the cut points to: a PC deletion of one of them would take that row.
        var pointedToByCloud = new HashSet<RelayRowKey>();
        if (cloudRows is not null)
        {
            foreach (var change in cloudWritten.Where(c => !c.Deleted))
            {
                if (cloudRows.TryGetValue(new RelayRowKey(change.Table, change.Key), out var json))
                {
                    pointedToByCloud.UnionWith(references(change.Table, json));
                }
            }
        }

        var memo = new Dictionary<RelayRowKey, bool>();

        // The cloud keeps this row's record: the row itself changed on both sides, or the record it belongs to is kept.
        bool CloudKeepsRecordOf(RelayRowKey key, int depth)
        {
            if (memo.TryGetValue(key, out var known))
            {
                return known;
            }

            var keeps = cloudByKey.ContainsKey(key) && rows.ContainsKey(key);
            if (!keeps && depth < 8 && OwnerOf(key) is { } parent && !RelayHandbackRules.Folders.Contains(parent.Table))
            {
                keeps = CloudKeepsRecordOf(parent, depth + 1);
            }

            memo[key] = keeps;
            return keeps;
        }

        var apply = new List<RelayRow>();
        var kept = new List<RelayRowKey>();
        foreach (var (key, row) in rows.OrderBy(r => r.Key.Table, StringComparer.Ordinal).ThenBy(r => r.Key.Key, StringComparer.Ordinal))
        {
            if (dropped.Contains(key) || RelayHandbackRules.NeverReturned.Contains(key.Table))
            {
                continue;
            }

            // The cloud changed this row — or the record it belongs to — too: the cloud's version stays, whatever the PC did
            // (an edit or a deletion), and the cabinet's is listed for somebody to apply — never written over the cloud's
            // work. A deletion that would take a row the cloud wrote since is kept the same way.
            var keepsRecord = CloudKeepsRecordOf(key, 0);
            if (keepsRecord || (row.Row is null && pointedToByCloud.Contains(key)))
            {
                if (!RelayHandbackRules.NotReviewed.Contains(key.Table))
                {
                    review.Add(new RelayPlannedReview(RelayReviewKind.BothChanged, key, null,
                        cloudByKey.TryGetValue(key, out var cloudChange) ? cloudChange.RecordedAtUtc : null));
                }

                kept.Add(key);
                continue;
            }

            apply.Add(row);
        }

        // AC-5.6 — what the cloud changed that the PC never received, and did not touch: kept, and listed.
        foreach (var (key, change) in cloudByKey.OrderBy(c => c.Value.Seq))
        {
            if (rows.ContainsKey(key) || pairedCloudKeys.Contains(key)
                || RelayHandbackRules.IsCloudOwned(key.Table) || RelayHandbackRules.NotReviewed.Contains(key.Table))
            {
                continue;
            }

            review.Add(new RelayPlannedReview(RelayReviewKind.CloudOnly, key, null, change.RecordedAtUtc));
        }

        return new RelayHandbackPlan(apply,
            dropped.OrderBy(k => k.Table, StringComparer.Ordinal).ThenBy(k => k.Key, StringComparer.Ordinal).ToList(), review, kept);
    }
}

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
}

/// <summary>A line the return lists (AC-5.6): the cabinet's key, and for a duplicate the cloud's own record of the same save.</summary>
public sealed record RelayPlannedReview(RelayReviewKind Kind, RelayRowKey Key, RelayRowKey? CloudKey, DateTime? CloudChangedAtUtc);

/// <summary>What the return does: the rows it applies, the duplicates it drops (D17) and the lines it lists (AC-5.6).</summary>
public sealed record RelayHandbackPlan(
    IReadOnlyList<RelayRow> Apply,
    IReadOnlyList<RelayRowKey> Dropped,
    IReadOnlyList<RelayPlannedReview> Review);

/// <summary>
/// The return's decisions (<c>clinic-pc-copy</c> D17, D18, EC-6, EC-7, FR-11), pure so each one is tested on its own.
///
/// <list type="bullet">
///   <item><b>The cabinet's version wins</b> (EC-7): a row the PC changed is applied even when the cloud changed it after
///   the PC's base — and the pair is listed, with the cloud's version, so nothing is lost silently.</item>
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
/// </summary>
public static class RelayHandbackPlanner
{
    public static RelayHandbackPlan Plan(
        RelayHandbackRequest request,
        IReadOnlyList<RelayCloudChange> cloudChanges,
        Func<string, JsonElement, IEnumerable<RelayRowKey>> references)
    {
        var rows = new Dictionary<RelayRowKey, RelayRow>();
        foreach (var row in request.Rows)
        {
            rows[new RelayRowKey(row.Table, row.Key)] = row;
        }

        var cloud = cloudChanges.Where(c => !c.FromRelay).ToList();
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

        var apply = new List<RelayRow>();
        foreach (var (key, row) in rows.OrderBy(r => r.Key.Table, StringComparer.Ordinal).ThenBy(r => r.Key.Key, StringComparer.Ordinal))
        {
            if (dropped.Contains(key) || RelayHandbackRules.NeverReturned.Contains(key.Table))
            {
                continue;
            }

            if (cloudByKey.TryGetValue(key, out var cloudChange))
            {
                if (!RelayHandbackRules.NotReviewed.Contains(key.Table))
                {
                    review.Add(new RelayPlannedReview(RelayReviewKind.BothChanged, key, null, cloudChange.RecordedAtUtc));
                }

                if (RelayHandbackRules.CloudKeepsIfChanged.Contains(key.Table))
                {
                    continue;
                }
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

        return new RelayHandbackPlan(apply, dropped.OrderBy(k => k.Table, StringComparer.Ordinal).ThenBy(k => k.Key, StringComparer.Ordinal).ToList(), review);
    }
}

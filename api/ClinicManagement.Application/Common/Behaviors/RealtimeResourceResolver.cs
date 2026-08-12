namespace ClinicManagement.Application.Common.Behaviors;

/// <summary>
/// Derives the real-time resource key for a MediatR request from its namespace: a mutating command in
/// <c>...Features.&lt;Area&gt;.Commands</c> maps to <c>&lt;area&gt;</c> lowercased; everything else — queries,
/// requests outside the feature/command convention, and excluded non-data areas — maps to null (no
/// broadcast). Pure and stateless, so the backend↔frontend key contract can be pinned by a test
/// (<c>RealtimeResourceResolverTests</c>).
///
/// The keys produced here MUST match <c>web/lib/realtime/clinic-hub.ts</c> <c>RealtimeResource</c> — a
/// client only refetches when the key it listens for matches the one broadcast. Renaming a
/// <c>Features/&lt;Area&gt;</c> folder changes the key; update the frontend map in lock-step (the contract
/// test fails when they drift).
/// </summary>
public static class RealtimeResourceResolver
{
    // Feature areas whose commands are not clinic data any list view mirrors — excluded so a login /
    // AI chat / backup does not emit a spurious refetch signal.
    //
    // "Dashboard" is here for the same reason plus a sharper one. Its only command saves ONE USER's layout
    // choices, and a broadcast goes to the whole `clinic-{id}` group — so hiding a card on your own dashboard
    // would tell every colleague's browser to refetch theirs. Per-user UI state is not clinic data, and the
    // clinic-wide bus is the wrong channel for it at any volume. (The dashboard *reads* still subscribe to the
    // nine data keys their figures depend on; that is unaffected — this excludes emitting, not listening.)
    //
    // "PushDevices" is the Dashboard case one step further out: its commands record which PHONE one user is
    // signed in on. A colleague registering their handset changes nothing on anybody's screen, so a broadcast
    // would tell every browser in the clinic to refetch over a fact none of them render — and would additionally
    // announce, clinic-wide, that somebody just signed in on a device.
    //
    // "Subscriptions" is excluded because FR-15 says the state is learned by a **re-read**, not by a broadcast, and
    // the two moments that change it cannot push one: a vendor grant runs in a separate process whose container does
    // not even resolve the notifier and which has no caller's token to derive a clinic from, and an entitlement
    // ending at midnight has no actor at all. A key here would advertise a live channel that covers neither.
    //
    // "Platform" is the vendor console, and BOTH pipeline defaults are wrong for it. The audience is derived from
    // the *acting user's* clinic, and a console account belongs to none — so a broadcast would reach nobody, and do
    // it silently. And the key would be a new one, which fails the contract test in both directions unless
    // `clinic-hub.ts` learns to listen for a resource no clinic screen renders. The cabinet learns of a grant by the
    // re-read `Subscriptions` is excluded for; the console's own screens are server-rendered per request.
    //
    // "Messaging" is `Subscriptions`' case verbatim, one feature over. Its two commands change a cabinet's WhatsApp
    // reminder forfait and are reachable ONLY from a `messaging-*` console verb — a separate process with no caller's
    // token and no notifier in its container — or from the vendor console, whose account belongs to no clinic. So the
    // audience the behavior derives would be nobody, silently, on both doors. The practice learns its new figure by the
    // ordinary re-read its « Rappels » screen already does; the counter that moves minutely is `ClinicMessagingMonth`,
    // which is not an aggregate root and emits nothing by design (D-6).
    private static readonly HashSet<string> ExcludedAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auth", "AI", "Backup", "Connectivity", "Dashboard", "Messaging", "Platform", "PushDevices", "Subscriptions"
    };

    public static string? Resolve(Type requestType)
    {
        var ns = requestType.Namespace;
        if (ns == null || !ns.EndsWith(".Commands", StringComparison.Ordinal))
        {
            return null;
        }

        const string marker = ".Features.";
        var start = ns.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = ns.IndexOf('.', start);
        if (end < 0)
        {
            return null;
        }

        var area = ns.Substring(start, end - start);
        return ExcludedAreas.Contains(area) ? null : area.ToLowerInvariant();
    }
}

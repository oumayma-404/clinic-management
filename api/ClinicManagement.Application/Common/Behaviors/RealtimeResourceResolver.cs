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
    private static readonly HashSet<string> ExcludedAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auth", "AI", "Backup", "Connectivity"
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

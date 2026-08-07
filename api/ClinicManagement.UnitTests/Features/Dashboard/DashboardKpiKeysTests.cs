using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Features.Dashboard;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// The dashboard-block <b>contract</b> between <see cref="DashboardKpiKeys"/> (the server-side write authority) and
/// <c>web/lib/dashboard-blocks.ts</c> (what the customiser offers and the page renders).
///
/// <para>
/// It exists for the same reason <c>RealtimeResourceResolverTests</c> does, and guards a failure with the same
/// shape. The two sides are independently editable and each looks complete on its own: the frontend's exhaustive
/// <c>Record&lt;DashboardBlockKey, …&gt;</c> forces a new block to get a label, and the server's
/// <c>UpdateDashboardPreferencesCommand</c> refuses anything not in <see cref="DashboardKpiKeys.All"/> — but
/// neither notices the other. Add a block on the frontend only and its switch is rendered, toggled, and then
/// <b>refused by the server</b> with « Élément(s) inconnu(s) », which surfaces as a failed save on an unrelated
/// card. Add one on the server only and it is offered by <c>availableKpis</c>, hideable in principle, and attached
/// to nothing.
/// </para>
/// <para>
/// Both directions are asserted, exactly, with no allow-list — the same "derived-vs-listed" lesson
/// <c>verify-schema</c> and the realtime contract test both embody.
/// </para>
/// </summary>
public class DashboardKpiKeysTests
{
    [Fact]
    public void EveryFrontendBlockKeyIsAcceptedByTheServer()
    {
        var frontend = FrontendBlockKeys();
        var backend = new SortedSet<string>(DashboardKpiKeys.All, StringComparer.Ordinal);

        var undeclared = frontend.Except(backend).ToList();

        Assert.True(
            undeclared.Count == 0,
            "web/lib/dashboard-blocks.ts declares these blocks but DashboardKpiKeys does not, so the customiser "
            + "renders a switch whose save the server refuses as an unknown element. Add each to "
            + "DashboardKpiKeys, or remove it from DASHBOARD_BLOCKS:\n  "
            + string.Join("\n  ", undeclared));
    }

    [Fact]
    public void EveryServerKeyIsRenderedByTheFrontend()
    {
        var frontend = FrontendBlockKeys();
        var backend = new SortedSet<string>(DashboardKpiKeys.All, StringComparer.Ordinal);

        var orphans = backend.Except(frontend).ToList();

        Assert.True(
            orphans.Count == 0,
            "DashboardKpiKeys accepts these keys but web/lib/dashboard-blocks.ts does not declare them, so they are "
            + "hideable through the API while corresponding to nothing on the dashboard:\n  "
            + string.Join("\n  ", orphans));
    }

    [Fact]
    public void NormalizeIsCaseInsensitiveAndCanonicalising()
    {
        // Two clients spelling the same intent differently must not produce two different stored rows.
        Assert.Equal(DashboardKpiKeys.LowStock, DashboardKpiKeys.Normalize("LOWSTOCK"));
        Assert.Equal(DashboardKpiKeys.LowStock, DashboardKpiKeys.Normalize("  lowStock  "));
        Assert.Null(DashboardKpiKeys.Normalize("notAKey"));
        Assert.Null(DashboardKpiKeys.Normalize(null));
        Assert.Null(DashboardKpiKeys.Normalize("  "));
    }

    [Fact]
    public void AllContainsNoDuplicates()
    {
        Assert.Equal(
            DashboardKpiKeys.All.Count,
            DashboardKpiKeys.All.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The block keys declared by <c>web/lib/dashboard-blocks.ts</c>, read out of its
    /// <c>DASHBOARD_BLOCKS</c> record.
    /// <para>
    /// Parsed from the <c>DASHBOARD_BLOCKS</c> object rather than the <c>DashboardBlockKey</c> type, deliberately:
    /// the type is a union that composes <c>DashboardKpiKey</c> from another file, so reading it would mean
    /// resolving TypeScript imports. The record is the thing the customiser actually iterates, and it is the
    /// exhaustive one — a key in the type but missing from the record is already a <c>tsc</c> error.
    /// </para>
    /// </summary>
    private static SortedSet<string> FrontendBlockKeys()
    {
        var source = File.ReadAllText(BlocksFilePath());

        var start = source.IndexOf("DASHBOARD_BLOCKS: Record<", StringComparison.Ordinal);
        Assert.True(
            start > -1,
            "the DASHBOARD_BLOCKS record is the frontend side of this contract; if it was renamed, update this test");

        var open = source.IndexOf('{', start);
        Assert.True(open > -1, "DASHBOARD_BLOCKS has no opening brace");

        // Walk braces to find the record's own closing brace — the entries contain nested `{ … }` objects, so a
        // naive search for the next '}' would stop at the first entry.
        var depth = 0;
        var end = -1;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                end = i;
                break;
            }
        }

        Assert.True(end > open, "DASHBOARD_BLOCKS appears to be unbalanced");

        var body = source[(open + 1)..end];

        // Only top-level keys: `key: { … }`. Nested property names (`section`, `label`, `hiddenByDefault`) sit
        // inside a brace pair and are excluded by requiring the value to start with '{'.
        var keys = Regex
            .Matches(body, @"(?m)^\s{2}([A-Za-z][A-Za-z0-9]*)\s*:\s*\{")
            .Select(m => m.Groups[1].Value);

        var set = new SortedSet<string>(keys, StringComparer.Ordinal);
        // A parse failure must not read as an empty, trivially-equal set — that would make both directions pass.
        Assert.NotEmpty(set);
        return set;
    }

    /// <summary>
    /// Locates <c>web/lib/dashboard-blocks.ts</c> from this source file's own compile-time path. Deliberately NOT
    /// from <c>AppContext.BaseDirectory</c>: the suite is routinely built to an output directory outside the
    /// repository (the Smart App Control workaround), which would make a walk-up from the binary fail.
    /// </summary>
    private static string BlocksFilePath([CallerFilePath] string thisFile = "")
    {
        const string relative = "web/lib/dashboard-blocks.ts";
        var native = relative.Replace('/', Path.DirectorySeparatorChar);

        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fail loudly. A contract test that skips when it cannot find one side is worse than no test: it reports
        // green while the contract it guards goes unchecked.
        throw new FileNotFoundException(
            $"Could not locate '{relative}' walking up from '{thisFile}'. The dashboard-block contract cannot be "
            + "verified without the frontend registry.");
    }
}

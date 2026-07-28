using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Features.AI.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Backup.Commands;
using MediatR;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Behaviors;

/// <summary>
/// The real-time resource-key <b>contract</b> between the backend and <c>web/lib/realtime/clinic-hub.ts</c>
/// (AC-P4.23–4.25).
///
/// <para><b>Why this is derived and not listed.</b> The previous version of this class was a hand-written
/// <c>[InlineData]</c> table of 17 command→key pairs. It could only fail if one of those 17 commands changed —
/// never on the case a guard exists for, a <i>new</i> feature area. That is how five backend keys
/// (<c>expenses</c>, <c>doctors</c>, <c>laborders</c>, <c>recall</c>, <c>waitinglist</c>) came to be broadcast
/// with no client listening while this test stayed green: audit finding § 9.1. It now derives both sides from
/// their authoritative sources — reflection over every MediatR request in the Application assembly, and the
/// frontend file itself — and asserts the two <b>sets are equal, in both directions</b>.
/// <c>verify-schema</c> is the same pattern applied to the database.</para>
///
/// <para>The only hand-maintained lists are the two allow-lists below, both empty, and any entry added to
/// either must carry the reason it is there.</para>
/// </summary>
public class RealtimeResourceResolverTests
{
    /// <summary>
    /// Keys the backend emits that <c>clinic-hub.ts</c> deliberately does not declare.
    ///
    /// <para><b>Empty on purpose.</b> A broadcast no client can name is a wasted signal at best and, more often,
    /// a screen that silently does not refresh — § 9.1's whole finding. If a new area genuinely should not be
    /// listened for, exclude it in <see cref="RealtimeResourceResolver"/> so it is never emitted, rather than
    /// emitting it and hiding it here.</para>
    /// </summary>
    private static readonly string[] EmitOnlyAllowList = Array.Empty<string>();

    /// <summary>
    /// Keys <c>clinic-hub.ts</c> declares that no backend command can emit.
    ///
    /// <para><b>Empty on purpose.</b> A declared key with no emitter is a subscription that can never fire, so a
    /// page wired to it looks live and is not. <c>documents</c> was the A-15 case: declared, emitted, and for a
    /// long while with no subscriber at all. Nothing belongs on this list.</para>
    /// </summary>
    private static readonly string[] ListenOnlyAllowList = Array.Empty<string>();

    // ---- The two sources of truth ------------------------------------------------

    /// <summary>
    /// Every resource key the backend can broadcast: each concrete MediatR request in the Application assembly,
    /// projected through the production resolver. Nulls (queries, excluded areas) drop out, which is what makes
    /// this the exact emitted set rather than a list of folder names.
    /// </summary>
    private static SortedSet<string> EmittedKeys()
    {
        var requests = typeof(RealtimeResourceResolver).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t));

        return new SortedSet<string>(
            requests.Select(RealtimeResourceResolver.Resolve).OfType<string>(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every key declared in the frontend's <c>RealtimeResource</c> map, read out of the file itself. Parsed
    /// rather than mirrored here: a copy of the frontend list in this file would rot exactly the way the
    /// <c>[InlineData]</c> table did.
    /// </summary>
    private static SortedSet<string> DeclaredKeys()
    {
        var path = ClinicHubPath();
        var source = File.ReadAllText(path);

        // Isolate `export const RealtimeResource = { … } as const` so unrelated string literals elsewhere in the
        // file (the event name, the hub path) cannot be mistaken for declared keys.
        var block = Regex.Match(
            source,
            @"export\s+const\s+RealtimeResource\s*=\s*\{(?<body>.*?)\}\s*as\s+const",
            RegexOptions.Singleline);

        Assert.True(
            block.Success,
            $"Could not find `export const RealtimeResource = {{ … }} as const` in {path}. If the map was renamed "
            + "or restructured, update this parser — do NOT replace it with a copy of the key list, which is the "
            + "failure mode this test exists to prevent.");

        var keys = Regex
            .Matches(block.Groups["body"].Value, @"^\s*\w+\s*:\s*""(?<key>[^""]+)""", RegexOptions.Multiline)
            .Select(m => m.Groups["key"].Value);

        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// Locates <c>web/lib/realtime/clinic-hub.ts</c> from this source file's own compile-time path. Deliberately
    /// NOT from <c>AppContext.BaseDirectory</c>: the suite is routinely built to an output directory outside the
    /// repository (the Smart App Control workaround), which would make a walk-up from the binary fail.
    /// </summary>
    private static string ClinicHubPath([CallerFilePath] string thisFile = "")
    {
        const string relative = "web/lib/realtime/clinic-hub.ts";
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
            $"Could not locate '{relative}' walking up from '{thisFile}'. The realtime contract cannot be "
            + "verified without the frontend key map.");
    }

    // ---- The contract: both directions, exact sets --------------------------------

    // [AC-P4.24] Every key the backend can emit is declared by the frontend. This is the direction that catches a
    // NEW feature area (P7's audit trail, P8's CNAM claims) broadcasting into the void.
    [Fact]
    public void Every_Emitted_Key_Is_Declared_By_The_Frontend()
    {
        var undeclared = EmittedKeys().Except(DeclaredKeys()).Except(EmitOnlyAllowList).ToList();

        Assert.True(
            undeclared.Count == 0,
            "The backend broadcasts these resource keys and web/lib/realtime/clinic-hub.ts does not declare them, "
            + "so no client can ever refetch on them (audit § 9.1). Add each to `RealtimeResource` and subscribe "
            + "the screens whose data depends on it — or, if the area should not broadcast at all, add it to "
            + "RealtimeResourceResolver's excluded areas:\n  "
            + string.Join("\n  ", undeclared));
    }

    // [AC-P4.24] …and the reverse: a declared key no command can produce is a subscription that never fires, so
    // the page looks live and is not (A-15).
    [Fact]
    public void Every_Declared_Key_Is_Emitted_By_A_Command()
    {
        var orphans = DeclaredKeys().Except(EmittedKeys()).Except(ListenOnlyAllowList).ToList();

        Assert.True(
            orphans.Count == 0,
            "web/lib/realtime/clinic-hub.ts declares these resource keys but no mutating command resolves to them, "
            + "so any screen subscribing to one will never refresh. Remove the key, or fix the "
            + "Features/<Area>/Commands folder name it was meant to mirror:\n  "
            + string.Join("\n  ", orphans));
    }

    // [AC-P4.25] The allow-lists are escape hatches, and an escape hatch nobody looks at becomes the norm. Pinning
    // them at empty makes widening the contract a deliberate edit to this file, with a reason attached.
    [Fact]
    public void Allow_Lists_Are_Empty()
    {
        Assert.Empty(EmitOnlyAllowList);
        Assert.Empty(ListenOnlyAllowList);
    }

    // A floor on the derivation itself: had reflection silently matched nothing (a renamed namespace convention, a
    // moved assembly), both set comparisons above would pass vacuously.
    [Fact]
    public void Derivation_Finds_A_Realistic_Number_Of_Keys()
    {
        var emitted = EmittedKeys();

        Assert.True(
            emitted.Count >= 15,
            $"Only {emitted.Count} broadcast keys were derived by reflection, which means the derivation is broken "
            + "rather than that the app shrank — the set-equality tests would then pass vacuously. Found: "
            + string.Join(", ", emitted));

        // Two keys with different shapes: a plain single-word area, and one whose folder name lower-cases into a
        // run-together word. If the resolver's namespace parsing regresses, these go first.
        Assert.Contains("appointments", emitted);
        Assert.Contains("treatmentplans", emitted);
    }

    // [AC-P4.23] An empty Features/<Area>/Commands folder holds no IRequest, so it must contribute no key.
    // `Features/AISummary/Commands` is exactly that — empty scaffolding — and a folder-scanning implementation of
    // this test would have wrongly demanded an `aisummary` key on both sides.
    [Fact]
    public void An_Empty_Commands_Folder_Contributes_No_Key()
    {
        Assert.DoesNotContain("aisummary", EmittedKeys());
    }

    // ---- The resolver's own rules ------------------------------------------------

    // Non-data command areas → null: a login, AI chat, or backup must not emit a refetch signal.
    [Theory]
    [InlineData(typeof(LoginCommand))]
    [InlineData(typeof(ChatCommand))]
    [InlineData(typeof(BackupNowCommand))]
    public void Resolve_Returns_Null_For_Excluded_Area(Type command)
        => Assert.Null(RealtimeResourceResolver.Resolve(command));

    // A query (not a .Commands namespace) is a read — it never broadcasts.
    [Fact]
    public void Resolve_Returns_Null_For_Query()
        => Assert.Null(RealtimeResourceResolver.Resolve(typeof(GetAppointmentsQuery)));
}

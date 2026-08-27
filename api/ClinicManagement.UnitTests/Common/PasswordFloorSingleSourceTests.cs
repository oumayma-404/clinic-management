using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>The password floor is stated in exactly one place</b> — <see cref="PasswordPolicy.MinLength"/> — and every
/// client reads it rather than restating it (<c>hosted-security-hardening</c> FR-1.9).
///
/// <para><b>The defect this exists to prevent.</b> Four screens that collect a <i>new</i> password each carried
/// their own <c>8</c>: <c>web/</c>'s change-password form, its join wizard, its setup wizard (which also serves
/// public signup) and <c>console/</c>'s change-password form. Raising the constant server-side would have left
/// each of them refusing at one number while the API refused at another — and, worse, <i>quoting the stale
/// number in a French sentence</i> to the user, who would then type a password the product accepts nowhere. The
/// floor is now served on <c>GET /api/auth/mode</c> (<c>passwordMinLength</c>) and, for the console — which
/// cannot reach that route, because <c>ConsolePortGate</c> 404s anything outside <c>/api/platform</c> on its
/// listener — on <c>GET /api/platform/auth/meta</c>.</para>
///
/// <para><b>⚠️ Why this cannot use <see cref="SolutionSources.Root"/>.</b> That walks up to
/// <c>ClinicManagement.sln</c>, which lives in <c>api/</c> — so it never sees <c>web/</c> or <c>console/</c> at
/// all, and a guard written on it would pass while checking nothing. The repository root is located from this
/// file's own compile-time path instead (<c>RealtimeResourceResolverTests.ClinicHubPath</c>'s pattern), and it
/// <b>throws</b> when it cannot find one rather than skipping.</para>
///
/// <para><b>⚠️ Why the patterns are anchored on password-length identifiers, not on numeric literals.</b> A bare
/// <c>8</c> or <c>12</c> matches durations, column counts, pixel sizes and page sizes across hundreds of files;
/// a check that noisy gets an exemption list, and an exemption list that grows is a check that has stopped
/// working. Each pattern below therefore requires the <i>password</i> to be named next to the number.</para>
/// </summary>
public class PasswordFloorSingleSourceTests
{
    /// <summary>
    /// The two client applications this rule covers, relative to the repository root.
    ///
    /// <para>Both, because the console cannot read the clinic app's endpoint and so had its own literal — and it
    /// is precisely the second application that gets forgotten.</para>
    /// </summary>
    private static readonly string[] ClientRoots = ["web", "console"];

    /// <summary>
    /// Files allowed to state a password length, each with the reason.
    ///
    /// <para>⚠️ Asserted equal in <b>both</b> directions, so this fails on a new violation <i>and</i> on an entry
    /// that no longer names a real one — a stale exemption is a pre-approved hole. It is deliberately empty:
    /// there is no legitimate reason for a client to know this number.</para>
    /// </summary>
    private static readonly Dictionary<string, string> AllowedToStateAFloor = new(StringComparer.Ordinal);

    /// <summary>
    /// A comparison of something <i>password</i>-shaped against a length.
    ///
    /// <para>⚠️ <c>(?!0\b)</c> is load-bearing: <c>password.length &gt; 0</c> is « did they type anything », not a
    /// floor, and four of those are live in the wizards. Excluding zero is what lets this stay anchored instead of
    /// acquiring an exemption for every non-empty check in the product.</para>
    /// </summary>
    private static readonly Regex LengthComparison = new(
        @"\w*(?:password|motdepasse)\w*\s*\.\s*length\s*(?:<=|>=|===|!==|==|<|>)\s*(?!0\b)\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A named constant holding the floor, the shape <c>web/</c> shipped (<c>MIN_PASSWORD_LENGTH = 8</c>).</summary>
    private static readonly Regex NamedConstant = new(
        @"\b(?:MIN|MAX)_?PASSWORD_?LENGTH\b\s*[:=]\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A field holding the floor as a literal (<c>passwordMinLength = 12</c>).
    ///
    /// <para>Requires a digit, so the DTO's own <c>passwordMinLength?: number</c> declaration — a type, not a
    /// value — is not a match.</para>
    /// </summary>
    private static readonly Regex LiteralAssignment = new(
        @"\bpassword\s*_?\s*min\s*_?\s*length\b\s*[:=]\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The French sentence with the number written into it — the console's own defect
    /// (« Au moins 8 caractères. »), and the one that reaches a user directly.
    ///
    /// <para>An interpolated « au moins {minLength} caractères » is not a match: the pattern requires digits.</para>
    /// </summary>
    private static readonly Regex FrenchSentence = new(
        @"au\s+moins\s+\d+\s+caract",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex[] Patterns = [LengthComparison, NamedConstant, LiteralAssignment, FrenchSentence];

    // [FR-1.9] The guarantee: no client restates the floor.
    [Fact]
    public void No_Client_States_A_Password_Length_Of_Its_Own()
    {
        var files = ClientSourceFiles();

        // "Found nothing" must not read as "nothing was wrong": a broken walk-up, a renamed folder or an
        // enumeration that silently yielded zero files would otherwise report this contract as satisfied.
        Assert.True(
            files.Count > 200,
            $"Only {files.Count} client source file(s) were scanned — the enumeration is broken, so this guard is "
            + "checking nothing. Fix the scan rather than trusting the green.");

        var violations = files
            .Where(f => StatesAFloor(File.ReadAllText(f.FullPath)))
            .Select(f => f.Relative)
            .Where(relative => !AllowedToStateAFloor.ContainsKey(relative))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "These client file(s) state a password length of their own: "
            + string.Join(", ", violations)
            + ". Read the served floor instead — `usePasswordMinLength()` in web/, `fetchAuthMeta` in console/ — "
            + "or the client will go on refusing at one number while the server refuses at another, and quote the "
            + "stale one to the user. PasswordPolicy.MinLength is the only place this number exists.");
    }

    // [FR-1.9] The other direction, on TenantScopeFilterTests' pattern: an exemption naming a file that no longer
    // violates anything is a hole standing open for whatever is written next.
    [Fact]
    public void Every_Exemption_Still_Names_A_Real_Violation()
    {
        var files = ClientSourceFiles().ToDictionary(f => f.Relative, f => f.FullPath, StringComparer.Ordinal);

        var stale = AllowedToStateAFloor.Keys
            .Where(relative => !files.TryGetValue(relative, out var path) || !StatesAFloor(File.ReadAllText(path)))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These exemption(s) no longer name a file that states a floor: " + string.Join(", ", stale)
            + ". Remove them — a stale exemption is a pre-approved hole.");
    }

    // [FR-1.9] The red proof, executed. A guard nobody has seen fail is a guard nobody knows works — and this one
    // is a regex, where the failure mode is silence rather than noise: a pattern that stopped matching would look
    // exactly like a codebase with no violations.
    [Fact]
    public void The_Patterns_Actually_Catch_The_Four_Shapes_That_Shipped()
    {
        // The literals as they really appeared, one per file that carried one.
        Assert.True(StatesAFloor("const MIN_PASSWORD_LENGTH = 8"), "the named constant web/ shipped");
        Assert.True(StatesAFloor("regPassword.length >= 8 &&"), "the join wizard's step-validity comparison");
        Assert.True(StatesAFloor("adminPassword.length > 0 && adminPassword.length < 8"), "the wizard's inline hint");
        Assert.True(StatesAFloor("<p>Au moins 8 caractères.</p>"), "the console's French sentence");
        Assert.True(StatesAFloor("passwordMinLength = 12"), "a literal assignment");

        // And it is discriminating rather than rejecting anything numeric near a password — these are the live
        // shapes in the wizards today, and flagging any of them would force an exemption list.
        Assert.False(StatesAFloor("regPassword.length > 0 && regPassword !== regPasswordConfirm"), "non-empty check");
        Assert.False(StatesAFloor("adminPasswordConfirm.length > 0"), "confirm-field non-empty check");
        Assert.False(StatesAFloor("minLength !== null && regPassword.length < minLength"), "the served value");
        Assert.False(StatesAFloor("Au moins {minLength} caractères."), "the interpolated sentence");
        Assert.False(StatesAFloor("passwordMinLength?: number;"), "the DTO's type declaration");
    }

    // [FR-1.9] The stripper's own red proof. Loosening a check to clear a false positive is how one silently
    // stops matching anything: the noisy direction is obvious, the silent one is indistinguishable from a clean
    // codebase. So each relaxation is proved to still catch the shape it was relaxed around.
    [Fact]
    public void Dropping_Comment_Lines_Does_Not_Blind_The_Guard()
    {
        // Prose describing the defect is not the defect — this is why the four fixed files stay green.
        Assert.False(StatesAFloor("// the form used to print « Au moins 8 caractères. » as a literal"), "line comment");
        Assert.False(StatesAFloor(" * used to print « Au moins 8 caractères. » as a literal"), "doc-comment body");

        // But a real literal in the same file is still caught, so the relaxation bought silence about comments
        // only — not about code.
        Assert.True(
            StatesAFloor("// we used to say « Au moins 8 caractères »\nplaceholder=\"Au moins 8 caractères\""),
            "a live placeholder beside a comment quoting it");

        // And a comment must not shield code that follows it on the SAME line, which is the direction a
        // strip-from-the-first-`//` implementation would fail in.
        Assert.True(
            StatesAFloor("const x = 1; // note\nif (password.length < 8) refuse()"),
            "code on a later line after a trailing comment");
        Assert.True(
            StatesAFloor("const url = \"https://x\"; if (password.length < 8) refuse()"),
            "a URL's // on the same line as a real violation");
    }

    private static bool StatesAFloor(string source) => Patterns.Any(p => p.IsMatch(WithoutCommentLines(source)));

    /// <summary>
    /// Drops whole-line comments before matching, so prose <i>describing</i> the defect is not itself flagged.
    ///
    /// <para><b>⚠️ Whole-line only, and that is what makes it safe rather than convenient.</b> A line whose first
    /// non-space characters are <c>//</c>, <c>/*</c>, <c>*</c> or <c>*/</c> cannot also carry executable code, so
    /// removing it can never hide a live literal — the failure mode a looser stripper would have. A <i>trailing</i>
    /// comment is deliberately left in place: stripping from the first <c>//</c> would also cut a URL inside a
    /// string (<c>https://…</c>) and could swallow a real violation later on the same line, which is precisely
    /// the silent direction this guard must not fail in.</para>
    ///
    /// <para>Without this, the four files that were <i>fixed</i> would be flagged for quoting « Au moins 8
    /// caractères » in the comment explaining why they no longer say it — and the only way to a green run would
    /// be to exempt them, i.e. to switch the guard off on exactly the files it exists for.</para>
    /// </summary>
    private static string WithoutCommentLines(string source)
    {
        var kept = source
            .Split('\n')
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                       && !trimmed.StartsWith("/*", StringComparison.Ordinal)
                       && !trimmed.StartsWith("*/", StringComparison.Ordinal)
                       && !trimmed.StartsWith("*", StringComparison.Ordinal);
            });

        return string.Join('\n', kept);
    }

    private sealed record ClientFile(string Relative, string FullPath);

    /// <summary>
    /// Every TypeScript source of the two client applications.
    ///
    /// <para>⚠️ <c>node_modules</c> and the build outputs are skipped by <b>not descending into them</b>, for the
    /// reason <see cref="SolutionSources.CsFiles"/> states: enumerating first and filtering after both doubles
    /// every hit and can throw before a single assertion runs.</para>
    /// </summary>
    private static IReadOnlyList<ClientFile> ClientSourceFiles()
    {
        var root = RepositoryRoot();
        var found = new List<ClientFile>();

        foreach (var clientRoot in ClientRoots)
        {
            var start = new DirectoryInfo(Path.Combine(root.FullName, clientRoot));

            Assert.True(
                start.Exists,
                $"'{clientRoot}/' was not found under '{root.FullName}'. This guard covers both client "
                + "applications; a missing one means it is silently checking half of what it claims.");

            var pending = new Stack<DirectoryInfo>();
            pending.Push(start);

            while (pending.Count > 0)
            {
                var directory = pending.Pop();

                foreach (var child in directory.EnumerateDirectories())
                {
                    if (child.Name is "node_modules" or ".next" or "dist" or "out" or "build" or "coverage")
                    {
                        continue;
                    }

                    pending.Push(child);
                }

                foreach (var file in directory.EnumerateFiles("*.ts").Concat(directory.EnumerateFiles("*.tsx")))
                {
                    var relative = Path.GetRelativePath(root.FullName, file.FullName)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    found.Add(new ClientFile(relative, file.FullName));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The repository root, from this file's own compile-time path.
    ///
    /// <para>Identified by holding <b>both</b> client applications, rather than by <c>.git</c> (absent in an
    /// archive export) or by <c>ClinicManagement.sln</c> (which is in <c>api/</c> and would resolve to a
    /// directory containing neither). Deliberately not <c>AppContext.BaseDirectory</c>: this suite is routinely
    /// built to an output directory outside the repository (the Smart App Control workaround).</para>
    /// </summary>
    private static DirectoryInfo RepositoryRoot([CallerFilePath] string thisFile = "")
    {
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "web", "package.json"))
                && File.Exists(Path.Combine(dir.FullName, "console", "package.json")))
            {
                return dir;
            }
        }

        // Fail loudly. A guard that skips when it cannot find its subject reports green while the contract it
        // covers goes unchecked — which is the exact failure this test was written to prevent elsewhere.
        throw new DirectoryNotFoundException(
            $"Could not locate the repository root (a directory holding both web/package.json and "
            + $"console/package.json) by walking up from '{thisFile}'. The password-floor guard cannot run, and "
            + "must fail rather than pass silently.");
    }
}

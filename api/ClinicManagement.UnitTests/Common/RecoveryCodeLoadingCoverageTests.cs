using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every path that touches an account's recovery codes reaches it through a read that LOADS them</b>
/// (<c>hosted-security-hardening</c> FR-1.4 / FR-1.5).
///
/// <para><b>Why a derived guard and not a behavioural test.</b> <c>ClinicTotpAuthTests</c> and
/// <c>PlatformAuthTests</c> prove the domain spends, replaces and counts codes correctly — and they do it over a
/// <see cref="ClinicManagement.Domain.Entities.User"/> built in memory with its codes already attached. That is
/// precisely the shape this guard exists for: the aggregate is right, the <i>query</i> that feeds it is not, and
/// nothing in a mock-repository suite can tell the difference.</para>
///
/// <para>⚠️ <b>The failure is silent in all four directions.</b> <c>RecoveryCodes</c> projects a private backing
/// list, so an unloaded collection is not stale — it is <b>empty</b>, and every question asked of it answers as
/// if the account held no codes. « Sécurité » reported « 0 code inutilisé » over eight live codes;
/// <c>ReplaceRecoveryCodes</c>' <c>Clear()</c> revoked nothing, so a regeneration <i>added</i> eight instead of
/// replacing them; <c>DisableTotp</c> left spendable rows behind an un-enrolled factor; and
/// <c>ConsumeRecoveryCode</c> matched nothing, so the one way back a user can take alone refused every code they
/// owned. No exception, no log line, no failing test — which is why the hole survived to be found on screen.</para>
///
/// <para>Both aggregates are checked in one place on purpose. <see cref="ClinicManagement.Domain.Entities.User"/>
/// and <see cref="ClinicManagement.Domain.Entities.PlatformAccount"/> are deliberate twins (see
/// <c>UserRecoveryCode</c>'s own note), and the defect this catches arrived as exactly that asymmetry: the
/// vendor's repository included the collection while the clinic's did not, for months, with both halves looking
/// correct on their own.</para>
///
/// <para>The candidate set is derived from <b>what the sources actually do</b> — which repository methods load
/// the collection, and which files call them — never from a list somebody remembered to keep up to date.</para>
/// </summary>
public class RecoveryCodeLoadingCoverageTests
{
    /// <summary>One aggregate that owns recovery codes, and the repository that reads it.</summary>
    private sealed record Aggregate(string Type, string RepositoryInterface, string RepositoryFile);

    private static readonly Aggregate[] Aggregates =
    [
        new("User", "IUserRepository", "UserRepository.cs"),
        new("PlatformAccount", "IPlatformAccountRepository", "PlatformAccountRepository.cs"),
    ];

    /// <summary>
    /// Files that touch the codes while obtaining their aggregate somewhere this guard cannot follow — handed in
    /// as a parameter, or read through a query of their own — each with the reason it is sound anyway.
    ///
    /// <para>⚠️ Asserted in <b>both</b> directions, so a stale entry fails too: an exemption that no longer names
    /// a real file is a pre-approved hole standing open for whatever is written next.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ResolvesItsAggregateElsewhere =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The members that only answer correctly over a <b>loaded</b> collection. Every one of them either reads the
    /// backing list or clears it, and both are wrong — differently — when it is empty.
    ///
    /// <para>⚠️ <b>Each is anchored on the receiver's dot, and the calls on the parens.</b> Without the dot,
    /// <c>AuthController</c>'s action <i>named</i> <c>DisableTotp</c> reads as a call on an aggregate the
    /// controller never loads (it is a thin MediatR pass-through); without the parens, a
    /// <c>&lt;see cref="PlatformAccount.IssueTotpSecret"/&gt;</c> in a doc comment does. Both were caught by this
    /// guard failing on files that touch nothing.</para>
    /// </summary>
    private static readonly Regex TouchesTheCodes = new(
        @"\.(?:ConsumeRecoveryCode|ReplaceRecoveryCodes|CompleteTotpEnrolment|IssueTotpSecret|DisableTotp)\s*\("
        + @"|\.UnusedRecoveryCodeCount\b",
        RegexOptions.Compiled);

    /// <summary>A repository method declaration, split into its awaited result and its name.</summary>
    private static readonly Regex RepositoryMethod = new(
        @"public\s+(?:async\s+)?(?:Task|ValueTask)<(?<ret>.*?)>\s+(?<name>\w+)\s*\(",
        RegexOptions.Compiled);

    /// <summary>The eager load. <c>Include(x =&gt; x.RecoveryCodes)</c> in any spelling of the lambda.</summary>
    private static readonly Regex LoadsTheCodes = new(
        @"Include\s*\(\s*\w+\s*=>\s*\w+\.RecoveryCodes\s*\)", RegexOptions.Compiled);

    // [FR-1.4][FR-1.5] The guarantee.
    [Fact]
    public void Every_Path_That_Touches_Recovery_Codes_Reads_Them_Through_A_Query_That_Loads_Them()
    {
        var root = SolutionSources.Root();
        var callers = CallersOfTheCodeSurface(root);

        // "Found nothing" must not read as "nothing was wrong": a renamed member or a broken scan would
        // otherwise report this contract as satisfied while checking no paths at all.
        Assert.True(
            callers.Count >= 6,
            $"Only {callers.Count} file(s) touching the recovery-code surface were found — the scan is broken, so "
            + "this guard is checking nothing. Fix it rather than trusting the green.");

        var reads = Aggregates.ToDictionary(a => a, a => ReadsOf(a, root));

        foreach (var (aggregate, (loading, all)) in reads)
        {
            // Same tripwire, one level down: an empty read set would silently exonerate every caller.
            Assert.True(
                all.Count >= 3 && loading.Count >= 1,
                $"{aggregate.RepositoryFile}: found {all.Count} read(s) returning {aggregate.Type}, "
                + $"{loading.Count} of them loading RecoveryCodes. The declaration scan is broken — a guard that "
                + "cannot see the repository's reads cannot hold anything to them.");
        }

        var unloaded = new List<string>();
        var unresolved = new List<string>();

        foreach (var file in callers)
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            var called = reads.ToDictionary(e => e.Key, e => ReadsCalledIn(source, e.Key, e.Value.All));

            if (called.Values.All(r => r.Count == 0))
            {
                // Touches the codes on an aggregate it obtained somewhere this guard cannot follow.
                if (!ResolvesItsAggregateElsewhere.ContainsKey(name))
                {
                    unresolved.Add(name);
                }

                continue;
            }

            unloaded.AddRange(
                called.SelectMany(entry => entry.Value
                    .Where(read => !reads[entry.Key].Loading.Contains(read))
                    .Select(read => $"{name} → {entry.Key.RepositoryInterface}.{read}")));
        }

        Assert.True(
            unloaded.Count == 0,
            "These path(s) touch an account's recovery codes through a read that does NOT load them: "
            + string.Join(", ", unloaded.Order(StringComparer.Ordinal))
            + ". The collection will be empty, so the count reads zero, a replacement revokes nothing and a "
            + "presented code matches nothing — all without an error. Add `.Include(x => x.RecoveryCodes)` to "
            + "that read, or use one that already has it.");

        Assert.True(
            unresolved.Count == 0,
            "These path(s) touch an account's recovery codes without going through a repository read this guard "
            + "can see: "
            + string.Join(", ", unresolved.Order(StringComparer.Ordinal))
            + ". Either load the aggregate through its repository, or record the file in "
            + $"{nameof(ResolvesItsAggregateElsewhere)} with the reason its collection is loaded anyway.");

        // Both directions: a reason that no longer names a real caller is a hole left open on purpose.
        var stale = ResolvesItsAggregateElsewhere.Keys
            .Where(name => !callers.Any(f => Path.GetFileName(f) == name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"{nameof(ResolvesItsAggregateElsewhere)} names file(s) that no longer touch the recovery-code "
            + "surface: " + string.Join(", ", stale) + ". Remove them.");
    }

    /// <summary>
    /// Every file under <c>Application</c> and <c>API</c> that touches the code surface. Those two layers only:
    /// <c>Domain</c> <i>is</i> the surface, and <c>Infrastructure</c> is where the reads being checked live.
    /// </summary>
    private static List<string> CallersOfTheCodeSurface(DirectoryInfo root) =>
        new[] { "ClinicManagement.Application", "ClinicManagement.API" }
            .Select(project => new DirectoryInfo(Path.Combine(root.FullName, project)))
            .Where(dir => dir.Exists)
            .SelectMany(SolutionSources.CsFiles)
            .Where(file => TouchesTheCodes.IsMatch(File.ReadAllText(file)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The repository's reads that return the aggregate, split by whether they load its codes. Derived from the
    /// implementation rather than the interface: <c>Include</c> is an implementation fact, and the interface
    /// cannot state it.
    /// </summary>
    private static (HashSet<string> Loading, HashSet<string> All) ReadsOf(Aggregate aggregate, DirectoryInfo root)
    {
        var file = SolutionSources
            .CsFiles(new DirectoryInfo(Path.Combine(root.FullName, "ClinicManagement.Infrastructure")))
            .FirstOrDefault(f => Path.GetFileName(f) == aggregate.RepositoryFile);

        Assert.NotNull(file);

        var source = File.ReadAllText(file);
        var declarations = RepositoryMethod.Matches(source);
        var returnsTheAggregate = new Regex($@"\b{aggregate.Type}\b", RegexOptions.Compiled);

        var loading = new HashSet<string>(StringComparer.Ordinal);
        var all = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < declarations.Count; i++)
        {
            if (!returnsTheAggregate.IsMatch(declarations[i].Groups["ret"].Value))
            {
                continue;
            }

            var name = declarations[i].Groups["name"].Value;
            all.Add(name);

            // The method's body: from this declaration to the next one, or to the end of the file.
            var start = declarations[i].Index + declarations[i].Length;
            var end = i + 1 < declarations.Count ? declarations[i + 1].Index : source.Length;

            if (LoadsTheCodes.IsMatch(source[start..end]))
            {
                loading.Add(name);
            }
        }

        return (loading, all);
    }

    /// <summary>
    /// Which of <paramref name="reads"/> this file calls <b>on that aggregate's repository</b>.
    ///
    /// <para>⚠️ Attributed by the receiver, never by the method name alone: <c>GetByIdAsync</c> and
    /// <c>GetByEmailAsync</c> exist on several repositories, and <c>EnrolTotpCommand</c> legitimately calls the
    /// <i>clinic</i> repository's. Matching on the name would fail that file for a read that has nothing to do
    /// with recovery codes.</para>
    /// </summary>
    private static List<string> ReadsCalledIn(string source, Aggregate aggregate, HashSet<string> reads)
    {
        // A field, a constructor parameter, or a service resolved into a local — the three ways this solution
        // gets hold of a repository.
        var holders = new Regex($@"{aggregate.RepositoryInterface}\s+(\w+)", RegexOptions.Compiled)
            .Matches(source)
            .Select(m => m.Groups[1].Value)
            .Concat(new Regex($@"var\s+(\w+)\s*=\s*[^;]*?<{aggregate.RepositoryInterface}>", RegexOptions.Compiled)
                .Matches(source)
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return reads
            .Where(read => holders.Any(holder =>
                Regex.IsMatch(source, $@"\b{Regex.Escape(holder)}\s*\.\s*{Regex.Escape(read)}\s*\(")))
            .ToList();
    }
}

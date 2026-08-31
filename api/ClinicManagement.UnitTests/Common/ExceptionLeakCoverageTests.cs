using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>No catch-all hands the client an exception's own message.</b>
///
/// <para><b>What this closes.</b> ~80 handlers ended <c>catch (Exception ex) when (ex is not
/// ConflictException)</c> with <c>Result.Failure(ex.Message)</c>. That string is rendered verbatim as
/// <c>{ error }</c>, so Npgsql SQLSTATEs and table names, S3 endpoints, server file paths and English framework
/// text all reached an authenticated browser — and, because nothing logged the exception, reached nowhere
/// else. The detail was simultaneously exposed where it must not go and lost where it was needed.</para>
///
/// <para>⚠️ <b>A TYPED catch is deliberately untouched.</b> <c>catch (ArgumentException ex)</c> and
/// <c>catch (InvalidOperationException ex)</c> in this codebase carry French domain text the handlers threw
/// themselves — « Ce patient a déjà une fiche ce jour-là », not machine output. Blanking those would replace
/// every precise refusal with « Une erreur est survenue », which is a worse product and no more secure. The
/// distinction is the exception's static type, which is why this scan reads it rather than guessing from the
/// message.</para>
///
/// <para>⚠️ <b>Brace-matched rather than line-based.</b> The failing form is routinely wrapped across three
/// lines, and a per-line scan would report the file as clean — which is how a first pass here rewrote 71 of the
/// 78 sites and reported success.</para>
/// </summary>
public class ExceptionLeakCoverageTests
{
    /// <summary>
    /// A catch-all: <c>catch (Exception ex)</c>, optionally filtered by the house
    /// <c>when (ex is not …Exception)</c>.
    ///
    /// <para>⚠️ <b>A generic catch with a DOMAIN filter is not a catch-all</b>, and reading it as one is a real
    /// mistake this scan made first time round. <c>catch (Exception ex) when
    /// (SubscriptionRefusals.IsDomainRefusal(ex))</c> catches only the domain's own French guards — « la durée
    /// doit être positive » — so its <c>ex.Message</c> is deliberate product copy, not machine text. Blanking
    /// those three replaced the vendor's precise refusals with « Une erreur est survenue » and reddened
    /// <c>GrantSubscriptionPeriodCommandHandlerTests</c>, which is the only reason it was caught. The filter is
    /// therefore matched narrowly: an <c>is not</c> exclusion still widens to everything, anything else
    /// narrows.</para>
    /// </summary>
    private static readonly Regex GenericCatch =
        new(@"catch\s*\(\s*Exception\s+(\w+)\s*\)(?<filter>\s*when\s*\(.*?\)\s*(?=\r?\n|\{))?",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The house filter — an exclusion, which still leaves the catch a catch-all.</summary>
    private static readonly Regex ExclusionFilter =
        new(@"^\s*when\s*\(\s*\w+\s+is\s+not\s+", RegexOptions.Compiled);

    /// <summary>
    /// Files allowed to hand a caught exception's <c>Message</c> to a <c>Result</c>, with the reason. Asserted
    /// <b>equal in both directions</b> — a stale entry fails as loudly as a new violation.
    ///
    /// <para><b>It is empty, and it should stay empty</b> — the same state <c>LogTemplateCoverageTests</c>'
    /// exemption map is in, and for the same reason: there is no legitimate case for putting machine text in a
    /// string the browser renders. <c>ExceptionMiddleware</c> is <i>not</i> an exemption, despite naming
    /// <c>ex.Message</c>: it passes it to <c>_logger.LogError</c>, which is the destination this guard exists to
    /// redirect everything else towards, and the predicate below only counts a <c>Failure(...)</c> argument.
    /// If an entry is ever needed here, read that as a sign the failure belongs in the log instead.</para>
    /// </summary>
    private static readonly Dictionary<string, string> AllowedByDesign = new(StringComparer.Ordinal);

    [Fact]
    public void No_Catch_All_Returns_The_Exceptions_Own_Message()
    {
        var offenders = ApplicationSources()
            .Where(f => LeaksInAGenericCatch(f.Source))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            AllowedByDesign.Keys.OrderBy(k => k, StringComparer.Ordinal),
            offenders);
    }

    /// <summary>
    /// The other half: the typed catches must still be carrying their French text. A « fix » that blanked those
    /// too would pass the test above and quietly turn every precise refusal in the product into « Une erreur est
    /// survenue » — so the count is asserted to stay substantial rather than merely non-zero.
    /// </summary>
    [Fact]
    public void The_Typed_Catches_Keep_Their_Own_French_Refusals()
    {
        var typed = ApplicationSources()
            .Sum(f => Regex.Matches(
                f.Source,
                @"catch\s*\(\s*(?:ArgumentException|InvalidOperationException)\s+(\w+)\s*(?:when[^)]*)?\)")
                .Count);

        Assert.True(typed > 40, $"Only {typed} typed catch(es) remain — the domain refusals have been blanked.");
    }

    /// <summary>
    /// Non-vacuity. A source scan fails <b>open</b>: a moved project or a changed catch shape leaves this class
    /// green for ever while checking nothing, which is how <c>SystemWideCallerCoverageTests</c>' console-verb
    /// branch matched nothing for two whole features.
    /// </summary>
    [Fact]
    public void The_Scan_Still_Finds_The_Catch_Alls()
    {
        var files = ApplicationSources().ToList();
        var catches = files.Sum(f => GenericCatch.Matches(f.Source).Count);

        Assert.True(files.Count > 200, $"Only {files.Count} Application source(s) found — the scan is blind.");
        Assert.True(catches > 150, $"Only {catches} generic catch(es) found — the scan has stopped matching.");
    }

    /// <summary>
    /// Red-proof: the guard must actually fire on the shape it describes, including the multi-line one that the
    /// first pass missed. Written as a probe rather than asking a reviewer to break a handler by hand.
    /// </summary>
    [Fact]
    public void The_Guard_Rejects_Both_Shapes_Of_The_Defect()
    {
        Assert.True(LeaksInAGenericCatch(
            "try { } catch (Exception ex) when (ex is not ConflictException) { return Result.Failure(ex.Message); }"));

        Assert.True(LeaksInAGenericCatch(
            "try { } catch (Exception ex) { return Result<int>.Failure(\n    $\"Échec : {ex.Message}\"); }"));

        // And stays quiet on the fixed form and on a typed catch.
        Assert.False(LeaksInAGenericCatch(
            "try { } catch (Exception ex) { return Result.Failure(ErrorMessages.Generic, ex); }"));

        Assert.False(LeaksInAGenericCatch(
            "try { } catch (ArgumentException ex) { return Result.Failure(ex.Message); }"));

        // ⚠️ And the case that made this necessary: a generic catch narrowed by a DOMAIN predicate carries the
        // domain's own French sentence, and must not be blanked.
        Assert.False(LeaksInAGenericCatch(
            "try { } catch (Exception ex) when (SubscriptionRefusals.IsDomainRefusal(ex)) "
            + "{ return Result.Failure(ex.Message); }"));
    }

    private static bool LeaksInAGenericCatch(string source)
    {
        foreach (Match match in GenericCatch.Matches(source))
        {
            var variable = match.Groups[1].Value;

            // ⚠️ A DOMAIN filter narrows a generic catch to the handler's own French guards, so its `ex.Message`
            // is deliberate product copy rather than machine text. An `is not X` filter is an EXCLUSION and
            // leaves the catch a catch-all, so it stays in scope. Getting this backwards blanked the vendor's
            // three subscription refusals — « la durée doit être positive » became « Une erreur est survenue ».
            var filter = match.Groups["filter"].Value;
            if (filter.Length > 0 && !ExclusionFilter.IsMatch(filter))
            {
                continue;
            }

            var body = BlockAfter(source, match.Index + match.Length);

            // The message reaching a `Result` is the leak. `_logger.LogError(ex, "…", ex.Message)` is the
            // destination, not the defect, so only a Failure(...) argument counts.
            if (Regex.IsMatch(
                    body,
                    @"Failure\([^;]*?" + Regex.Escape(variable) + @"\.Message",
                    RegexOptions.Singleline))
            {
                return true;
            }
        }

        return false;
    }

    private static string BlockAfter(string source, int from)
    {
        var open = source.IndexOf('{', from);
        if (open < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }

        return source[open..];
    }

    private static IEnumerable<(string Name, string Source)> ApplicationSources()
    {
        var root = SolutionSources.Root();

        foreach (var file in SolutionSources.CsFiles(root))
        {
            // The test project is excluded: this class quotes the defective shapes above, and a guard that
            // fails on its own red-proof is a guard nobody can write.
            if (file.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.UnitTests{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!file.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.Application{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return (Path.GetFileName(file), File.ReadAllText(file));
        }
    }
}

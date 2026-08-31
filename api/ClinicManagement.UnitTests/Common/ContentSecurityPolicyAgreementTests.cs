using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ClinicManagement.API.Middleware;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The content-security policy is stated in <b>three</b> places and they must be identical
/// (<c>hosted-security-hardening</c> FR-4.5's last clause).
///
/// <list type="number">
///   <item><see cref="SecurityHeadersMiddleware"/> — covers what Kestrel serves, which behind the hosted reverse
///   proxy is <c>/api/*</c> alone.</item>
///   <item><c>deploy/Caddyfile</c> — twice: the public site's page responses, and the vendor console's.</item>
///   <item><c>console/next.config.ts</c> — the console reached without the proxy in front of it.</item>
/// </list>
///
/// <para><b>Why a guard and not a comment.</b> The middleware's own docstring has said « the two are
/// byte-identical and must be changed together » since <c>multi-tenant-cloud</c> US-6, and <b>nothing enforced
/// it</b>. Drift here is invisible in the worst way: each copy is valid on its own, the app works, and the only
/// symptom is that one surface enforces a policy nobody chose — which for the console is the surface reached by
/// the account that can read every cabinet in the deployment.</para>
///
/// <para>⚠️ <b>It parses the real files</b> rather than comparing constants, because a constant here would be a
/// fourth copy — and the one most likely to be updated in the same edit that was supposed to be checked. The
/// files are found through <c>[CallerFilePath]</c>, not <c>AppContext.BaseDirectory</c>: this suite is routinely
/// built to a scratch <c>OutDir</c> outside the repo (the Smart App Control workaround), so the base directory
/// is not in the tree at all. Same technique and same reason as
/// <c>RealtimeResourceResolverTests</c>.</para>
///
/// <para>⚠️ It <b>throws</b> rather than skipping when a file is missing, for that class's reason too: a guard
/// that quietly finds nothing to check is indistinguishable from one that passes.</para>
/// </summary>
public class ContentSecurityPolicyAgreementTests
{
    /// <summary>The middleware's own constant — the value this whole test measures the other copies against.</summary>
    private static string MiddlewarePolicy =>
        (string)typeof(SecurityHeadersMiddleware)
            .GetField("ContentSecurityPolicy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    [Fact]
    public void The_Middleware_Policy_Is_Not_Empty()
    {
        // Non-vacuity first: every assertion below compares against this, so an empty value would make the
        // whole class pass while checking nothing (SystemWideCallerCoverageTests' lesson).
        Assert.NotEmpty(MiddlewarePolicy);
        Assert.Contains("default-src 'self'", MiddlewarePolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_Caddy_Sites_Serve_The_Middlewares_Policy_Byte_For_Byte()
    {
        var policies = CaddyPolicies();

        // Two, and the count is asserted: the console site gained its first policy in Part D, and a future edit
        // that removes it would otherwise leave this class green while checking only the public one.
        Assert.Equal(2, policies.Count);
        Assert.All(policies, policy => Assert.Equal(MiddlewarePolicy, policy));
    }

    [Fact]
    public void The_Console_Next_Config_Serves_The_Middlewares_Policy_Byte_For_Byte()
    {
        Assert.Equal(MiddlewarePolicy, ConsoleConfigPolicy());
    }

    /// <summary>
    /// <c>'unsafe-eval'</c> is gone and stays gone (FR-4.5's « the weakest directive is removed »). Asserted
    /// separately from the equality above because all three copies agreeing on a weak policy is exactly the
    /// state this feature ended.
    ///
    /// <para>⚠️ <b>Matched as a TOKEN, not as a substring, and the difference is the whole point of this
    /// change.</b> <c>'wasm-unsafe-eval'</c> <i>contains</i> the text <c>unsafe-eval</c> while being a far
    /// narrower grant — it permits <c>WebAssembly.compile</c> and nothing else, where <c>'unsafe-eval'</c>
    /// permits <c>eval()</c>, <c>new Function()</c> and string <c>setTimeout</c>. A substring check cannot tell
    /// them apart, so it would have forced the coffre's author to choose between a broken feature and deleting
    /// this guard — and a guard that punishes the correct fix gets deleted rather than fixed.</para>
    ///
    /// <para>The tokens are split on whitespace and compared whole, so <c>'unsafe-eval'</c> is caught wherever
    /// in the policy it appears and <c>'wasm-unsafe-eval'</c> is not mistaken for it.</para>
    /// </summary>
    [Fact]
    public void The_Policy_Permits_No_Eval()
    {
        var tokens = MiddlewarePolicy
            .Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.DoesNotContain("'unsafe-eval'", tokens, StringComparer.Ordinal);
    }

    /// <summary>
    /// The narrow token IS present, and this is not decoration: without it the coffre is dead on arrival
    /// wherever the policy is enforced — <c>ingestIntoVault</c> compiles a WebAssembly SHA-256 as its very first
    /// step, before a single byte is written. It failed silently in the sense that mattered: the hosted upload
    /// path has no WebAssembly in it, so ordinary files kept working and only 3D studies broke.
    ///
    /// <para>Asserted so that « tidying » the policy cannot remove it without a red test naming the feature it
    /// would break.</para>
    /// </summary>
    [Fact]
    public void The_Policy_Permits_WebAssembly_Which_The_Coffre_Needs()
    {
        Assert.Contains("'wasm-unsafe-eval'", MiddlewarePolicy, StringComparison.Ordinal);
    }

    /// <summary>
    /// A violation has somewhere to go (FR-4.5). Both mechanisms, because browsers disagree about which one they
    /// implement and a report nobody receives is the state this replaced.
    /// </summary>
    [Fact]
    public void The_Policy_Names_A_Report_Destination()
    {
        Assert.Contains("report-uri /api/csp-report", MiddlewarePolicy, StringComparison.Ordinal);
        Assert.Contains("report-to csp-endpoint", MiddlewarePolicy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The executed red proof. It runs the <b>real</b> parsers over a copy of the real Caddyfile with one
    /// directive changed, rather than asking a reviewer to edit a file by hand — the shape
    /// <c>ConsoleVerbDispatchTests</c> and <c>SecretProtectionCoverageTests</c> both use.
    /// </summary>
    [Fact]
    public void The_Guard_Rejects_A_Caddy_Site_Whose_Policy_Drifts()
    {
        var drifted = File.ReadAllText(CaddyfilePath())
            .Replace("frame-ancestors 'none'", "frame-ancestors 'self'", StringComparison.Ordinal);

        var policies = ExtractCaddyPolicies(drifted);

        Assert.Equal(2, policies.Count);
        Assert.Contains(policies, policy => policy != MiddlewarePolicy);
    }

    /// <summary>And the same proof for the console's own config, which is parsed differently.</summary>
    [Fact]
    public void The_Guard_Rejects_A_Console_Config_Whose_Policy_Drifts()
    {
        var drifted = File.ReadAllText(ConsoleConfigPath())
            .Replace("object-src 'self' blob:", "object-src *", StringComparison.Ordinal);

        Assert.NotEqual(MiddlewarePolicy, ExtractConsoleConfigPolicy(drifted));
    }

    // ---------------------------------------------------------------- parsing

    private static IReadOnlyList<string> CaddyPolicies() => ExtractCaddyPolicies(File.ReadAllText(CaddyfilePath()));

    /// <summary>
    /// Every <c>Content-Security-Policy "…"</c> directive in the file. Caddy's own syntax quotes a value
    /// containing spaces, so the double quote is the delimiter and the policy itself contains none.
    /// </summary>
    private static IReadOnlyList<string> ExtractCaddyPolicies(string caddyfile) =>
        Regex.Matches(caddyfile, "Content-Security-Policy\\s+\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

    private static string ConsoleConfigPolicy() => ExtractConsoleConfigPolicy(File.ReadAllText(ConsoleConfigPath()));

    /// <summary>
    /// The <c>CSP</c> constant the config's <c>headers()</c> returns. Matched on the declaration rather than on
    /// the header entry, since the entry names the constant and not the value.
    /// </summary>
    private static string ExtractConsoleConfigPolicy(string config)
    {
        var match = Regex.Match(config, "const CSP\\s*=\\s*\"([^\"]+)\"\\s*;");

        Assert.True(match.Success, "console/next.config.ts declares no `const CSP = \"…\";`.");
        return match.Groups[1].Value;
    }

    // ---------------------------------------------------------------- locating the repo

    private static string CaddyfilePath() => RepoFile(Path.Combine("deploy", "Caddyfile"));

    private static string ConsoleConfigPath() => RepoFile(Path.Combine("console", "next.config.ts"));

    private static string RepoFile(string relativePath)
    {
        // `Root()` is the `api/` directory (it holds the .sln), so the repository is its parent.
        var path = Path.Combine(SolutionSources.Root().Parent!.FullName, relativePath);

        if (!File.Exists(path))
        {
            // Throwing, not skipping: a missing file means this check silently stopped covering the thing it
            // was written for, which is worse than a red build.
            throw new FileNotFoundException(
                $"Cannot find '{relativePath}' — the CSP agreement guard has nothing to compare against.", path);
        }

        return path;
    }
}

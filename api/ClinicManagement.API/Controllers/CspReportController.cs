using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Receives Content-Security-Policy violation reports (<c>hosted-security-hardening</c> FR-4.5).
///
/// <para><b>It exists because an enforcing policy with no reporting is a control nobody can tune.</b> The policy
/// shipped report-only for the life of the deployment and the reports went to each browser's own console — that
/// is, to nobody. Turning enforcement on without this would make the first violation a broken screen for a
/// clinic and a silence for the operator.</para>
///
/// <para>⚠️ <b>The report's <c>document-uri</c> is stripped to its ROUTE PATTERN before anything is recorded</b>,
/// and that is not tidiness: this application's addresses carry patient identifiers
/// (<c>/patients/3f2a…/files</c>), so a report body is PHI and reports are subject to FR-4.4 like any other log
/// line. What an operator needs is « the policy broke on the patient files screen », which the pattern says
/// exactly.</para>
///
/// <para>⚠️ <b>Anonymous, and it has to be.</b> A violation frequently fires on the login page, before any
/// session exists — the report that matters most is the one from the screen nobody could get past.</para>
///
/// <para>⚠️ <b>Excess is DROPPED, never stored.</b> One misbehaving extension on one machine can emit a report
/// per navigation; the rate limiter answers 429 and nothing is written, which is the right outcome for a
/// diagnostic feed. It always answers <c>204</c> otherwise — a browser does nothing with the status, and a
/// malformed body is not worth a round trip to say so.</para>
/// </summary>
[ApiController]
[Route("api/csp-report")]
[AllowAnonymous]
[EnableRateLimiting(RateLimiting.CspReportPolicy)]
public class CspReportController : ControllerBase
{
    /// <summary>Longer than this is not a report, it is somebody using the endpoint as storage.</summary>
    private const int MaxBodyBytes = 8 * 1024;

    /// <summary>
    /// Every path segment that is an id — a GUID, a number, or a long opaque token — becomes <c>{id}</c>.
    /// Matching on the <b>shape</b> rather than on a list of known routes is what keeps this true for a route
    /// added later, which is the same reason the audit interceptor derives its candidates.
    /// </summary>
    private static readonly Regex IdSegment = new(
        @"/(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\d+|[A-Za-z0-9_-]{24,})(?=/|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly ILogger<CspReportController> _logger;

    public CspReportController(ILogger<CspReportController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/csp-report", "application/reports+json", "application/json")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var body = await ReadBoundedAsync(cancellationToken);
        if (body is null)
        {
            return NoContent();
        }

        foreach (var (directive, blocked, document) in Parse(body))
        {
            // Warning, not Information: an enforcing policy refusing something is either an attack or a screen
            // that is now broken for a clinic, and both are worth finding in a log.
            _logger.LogWarning(
                "CSP violation: {Directive} blocked {BlockedUri} on {DocumentRoute}",
                directive, blocked, document);
        }

        return NoContent();
    }

    private async Task<string?> ReadBoundedAsync(CancellationToken cancellationToken)
    {
        // Bounded before it is read, not after: `ReadToEndAsync` on an unbounded body is the thing the cap is
        // for. Anything longer is dropped whole rather than truncated into unparseable JSON.
        var buffer = new byte[MaxBodyBytes];
        var read = await Request.Body.ReadAtLeastAsync(
            buffer, MaxBodyBytes, throwOnEndOfStream: false, cancellationToken);

        return read is 0 or MaxBodyBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// Both shapes, because browsers disagree: the legacy <c>{"csp-report": {…}}</c> that <c>report-uri</c>
    /// produces, and the <c>[{"type":"csp-violation","body":{…}}]</c> array that <c>report-to</c> does. An
    /// unparseable body yields nothing at all — this is a diagnostic feed, and a parser that throws would turn
    /// somebody's malformed report into an entry in the log it exists to keep readable.
    /// </summary>
    private static IEnumerable<(string Directive, string Blocked, string Document)> Parse(string body)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var legacy))
        {
            yield return Read(legacy, "violated-directive", "blocked-uri", "document-uri");
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var report in root.EnumerateArray())
        {
            if (report.ValueKind == JsonValueKind.Object && report.TryGetProperty("body", out var reportBody))
            {
                yield return Read(reportBody, "effectiveDirective", "blockedURL", "documentURL");
            }
        }
    }

    private static (string Directive, string Blocked, string Document) Read(
        JsonElement report, string directiveKey, string blockedKey, string documentKey) =>
        (Text(report, directiveKey), Text(report, blockedKey), RouteOf(Text(report, documentKey)));

    private static string Text(JsonElement report, string property) =>
        report.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "(none)"
            : "(none)";

    /// <summary>
    /// The address reduced to what it is safe to keep: the path's shape, with every id replaced and the query
    /// string dropped whole. A query carries search terms, which on this product are patients' names.
    /// </summary>
    private static string RouteOf(string documentUri)
    {
        if (!Uri.TryCreate(documentUri, UriKind.Absolute, out var uri))
        {
            return "(none)";
        }

        try
        {
            return IdSegment.Replace(uri.AbsolutePath, "/{id}");
        }
        catch (RegexMatchTimeoutException)
        {
            // A crafted path that defeats the matcher must not leave the raw address in the log — which is the
            // one outcome this whole method exists to prevent.
            return "(illisible)";
        }
    }
}

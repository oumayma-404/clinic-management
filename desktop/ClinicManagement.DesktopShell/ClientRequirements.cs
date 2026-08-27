using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// What this server requires of a client, read over plain HTTP <b>before the web app is loaded</b> — the desktop
/// counterpart of the Android and iOS shells' probe of the same route.
///
/// <para>
/// ⚠️ <b>The desktop shell was outside the version floor entirely until this existed.</b> The mobile shells send
/// <c>X-Client-Version</c> through <c>window.__clinicShell</c>, which the web bundle reads when it builds every
/// request header; the WPF shell has no bridge, so it sent nothing, and <c>ClientVersionMiddleware</c> — which
/// treats an absent header as « a browser, accept it » — never refused it. A clinic could therefore run a shell
/// arbitrarily older than the server with no way for either side to say so.
/// </para>
///
/// <para>
/// ⚠️ <b>Unreadable means "no floor", never "refuse"</b> — the same direction the server's own
/// <c>ClientRequirements.IsBelowFloor</c> and both mobile shells take, and for the same reason: an offline PC, a
/// server too old to have the route, a malformed body and an unset floor must all pass. A shell that refuses to
/// start because a probe failed is a worse outcome than any it could prevent, and the unreachable case is
/// diagnosed far better by the navigation that follows than by this probe.
/// </para>
/// </summary>
public static class ClientRequirements
{
    /// <summary>
    /// The route asked for. Anonymous, and the one <c>/api</c> path exempt from the floor it publishes — so a
    /// shell already too old can still read where to get a newer one.
    /// </summary>
    private const string Path = "/api/meta/client-requirements";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>What the probe learned. Every field is blank when the server said nothing about it.</summary>
    public sealed record Requirements(string MinimumShellVersion, string CurrentShellVersion, string DownloadUrl);

    /// <summary>
    /// This build's version, from the assembly — so the shell cannot report a version it was not built as.
    /// <c>&lt;Version&gt;</c> in the csproj is the single source.
    /// </summary>
    public static string InstalledVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";

    /// <summary><c>null</c> when the answer could not be read at all — see the type note on why that must pass.</summary>
    public static async Task<Requirements?> FetchAsync(string baseUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            // Sent here too, so the one route exempt from the floor is still asked the same question every other
            // call asks — and so a server log shows which build is calling.
            client.DefaultRequestHeaders.Add("X-Client-Version", InstalledVersion);
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            using var response = await client.GetAsync(baseUrl + Path).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            return new Requirements(
                ReadString(root, "minimumShellVersion"),
                ReadString(root, "currentShellVersion"),
                root.TryGetProperty("storeUrls", out var stores) ? ReadString(stores, "windows") : string.Empty);
        }
        catch (Exception)
        {
            // Offline, DNS, TLS, a 404 on an older server, malformed JSON — all of them mean "no floor".
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="installed"/> is older than <paramref name="other"/>. <b>False for anything
    /// unparseable</b>, mirroring the server's <c>Version.TryParse</c> pair and both mobile shells, so no two
    /// sides can disagree about which builds are acceptable.
    /// </summary>
    public static bool IsOlderThan(string installed, string other)
    {
        var left = Parse(installed);
        var right = Parse(other);
        if (left is null || right is null)
        {
            return false;
        }

        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
        {
            var a = index < left.Count ? left[index] : 0;
            var b = index < right.Count ? right[index] : 0;
            if (a != b)
            {
                return a < b;
            }
        }

        return false;
    }

    /// <summary><c>1.2.3</c> → <c>[1, 2, 3]</c>. Null for anything that is not a dotted run of non-negative integers.</summary>
    private static List<int>? Parse(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var parts = trimmed.Split('.');
        if (parts.Length > 4)
        {
            return null;
        }

        var numbers = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var number) || number < 0)
            {
                return null;
            }

            numbers.Add(number);
        }

        return numbers;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}

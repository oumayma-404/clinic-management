using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// The app follows the PC de secours (<c>clinic-pc-copy</c> Part 3, since 1.6): while online it holds a session prepared
/// on the PC (D22), and when the cloud cannot be reached and the PC says it holds the cabinet's saves, the window moves
/// there — already signed in — and back once the PC lets go.
///
/// <para>⚠️ <b>One holder only.</b> The session traded here is written straight into the WebView's cookie for the PC's
/// origin and never refreshed by the shell: the PC's refresh chain treats a reused credential as theft, so a second
/// holder would sign the person out before the next cut. Each day the page brings a fresh ticket and the cookie is
/// replaced.</para>
///
/// <para>⚠️ The PC is recognised by its certificate (D21), as <see cref="RelayProbe"/> does: only the private addresses
/// the cloud named, and only behind the PC's own certificate.</para>
/// </summary>
public static class RelaySwitch
{
    /// <summary>
    /// Where the PC de secours answers: one address, its port, its certificate — and which relay it is, so the app can
    /// find it again by UDP discovery when the box gives it a new address (D21). A target saved before that has no id.
    /// </summary>
    public sealed record Target(string Address, int Port, string Fingerprint, Guid? RelayId = null)
    {
        public string Host => IPAddress.TryParse(Address, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{Address}]"
            : Address;

        public string Origin => $"https://{Host}:{Port}";
    }

    public sealed record Prepare(string Assertion, RelayProbe.Request Where, Guid? RelayId = null);

    public sealed record Session(string Credential, DateTime? ExpiresAtUtc, bool MustChangePassword);

    /// <summary>After this long with the cloud unreachable and the PC not holding, the PC was not ready (AC-3.8).</summary>
    public static readonly TimeSpan TakeoverExpectedWithin = TimeSpan.FromMinutes(3);

    public const string SessionCookie = "__Host-local_session";
    public const string MustChangeCookie = "__Host-local_must_change_password";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);

    // ---- the page's request ---------------------------------------------------------------------------------

    /// <summary>The page's <c>relayPrepare</c> payload: the cloud's ticket and where the PC is. Null on anything else.</summary>
    public static Prepare? ParsePrepare(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var assertion = root.TryGetProperty("assertion", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            if (assertion is null || !assertion.StartsWith("ra1.", StringComparison.Ordinal) || assertion.Length > 4096)
            {
                return null;
            }

            Guid? relayId = root.TryGetProperty("relayId", out var r) && r.ValueKind == JsonValueKind.String
                            && Guid.TryParse(r.GetString(), out var id)
                ? id
                : null;
            return RelayProbe.Parse(json) is { } where ? new Prepare(assertion, where, relayId) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The session in the PC's answer (bare, or inside the API's <c>value</c> envelope). Null when there is none.</summary>
    public static Session? ParseSession(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
            {
                root = value;
            }

            var credential = root.TryGetProperty("refreshToken", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            if (string.IsNullOrEmpty(credential))
            {
                return null;
            }

            DateTime? expires = root.TryGetProperty("refreshExpiresAt", out var e) && e.ValueKind == JsonValueKind.String
                                && e.TryGetDateTime(out var at)
                ? at.ToUniversalTime()
                : null;
            var mustChange = root.TryGetProperty("mustChangePassword", out var m) && m.ValueKind == JsonValueKind.True;
            return new Session(credential, expires, mustChange);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What the unreachable screen adds while the PC answers but does not hold (AC-3.3, then AC-3.8).</summary>
    public static string WaitingLine(DateTime cloudLostAtUtc, DateTime nowUtc)
    {
        var elapsed = nowUtc - cloudLostAtUtc;
        if (elapsed >= TakeoverExpectedWithin)
        {
            return "Le PC de secours n'était pas à jour : il ne peut pas prendre le relais.";
        }

        // Counted from when THIS app lost the cloud — close to the PC's own count, which starts at its last answer.
        var left = (int)Math.Ceiling((TakeoverAfter - elapsed).TotalSeconds / 5) * 5;
        return left > 0
            ? $"Internet coupé — le PC de secours prend le relais dans {left} secondes."
            : "Internet coupé — le PC de secours prend le relais dans quelques instants.";
    }

    /// <summary>When the PC de secours takes over after its last answer from the cloud (<c>ClinicWriteLease.PcTakesOverAfter</c>).</summary>
    public static readonly TimeSpan TakeoverAfter = TimeSpan.FromSeconds(60);

    // ---- where the PC is, kept between runs -----------------------------------------------------------------

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClinicManagement", "relay-target.json");

    private static Target? _current;
    private static bool _loaded;

    /// <summary>The PC this app prepared a session on, or null — read once, then kept in step with <see cref="Save"/>.</summary>
    public static Target? Current
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                try
                {
                    _current = File.Exists(FilePath) ? JsonSerializer.Deserialize<Target>(File.ReadAllText(FilePath)) : null;
                }
                catch
                {
                    _current = null;
                }
            }

            return _current;
        }
    }

    public static void Save(Target target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(target));
        _current = target;
        _loaded = true;
    }

    /// <summary>True when <paramref name="uri"/> is the PC de secours's own origin — host and port, like the server's.</summary>
    public static bool IsPcOrigin(Uri uri, Target? target) =>
        target is not null
        && string.Equals(uri.Host.Trim('[', ']'), target.Address, StringComparison.OrdinalIgnoreCase)
        && uri.Port == target.Port;

    // ---- the PC, through its pinned certificate ---------------------------------------------------------------

    /// <summary>Trades the ticket on the first address that answers with the PC's certificate. Never throws.</summary>
    public static async Task<(Target Target, Session Session)?> TradeAsync(Prepare request)
    {
        foreach (var address in request.Where.Addresses)
        {
            var target = new Target(address.ToString(), request.Where.Port, request.Where.Fingerprint, request.RelayId);
            try
            {
                using var client = Client(target);
                using var body = new StringContent(
                    JsonSerializer.Serialize(new { assertion = request.Assertion }), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync($"{target.Origin}/api/auth/relay-session", body);
                if (response.IsSuccessStatusCode && ParseSession(await response.Content.ReadAsStringAsync()) is { } session)
                {
                    return (target, session);
                }
            }
            catch
            {
                // The next address — or nothing: the page prepares again tomorrow.
            }
        }

        return null;
    }

    /// <summary>Whether the PC holds the cabinet's saves: true / false, or null when it cannot be reached as itself.</summary>
    public static async Task<bool?> HoldingAsync(Target target)
    {
        try
        {
            using var client = Client(target);
            using var response = await client.GetAsync($"{target.Origin}/api/relay/local/holding");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.TryGetProperty("holding", out var holding) && holding.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient Client(Target target)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                RelayProbe.IsThePcsCertificate(certificate?.RawData, target.Fingerprint),
            UseProxy = false,
            AllowAutoRedirect = false,
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = AttemptTimeout };
    }
}

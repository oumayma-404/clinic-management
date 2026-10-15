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

    /// <summary>Said beside « Travailler sur le PC de secours » (manual takeover, owner 2026-10-12).</summary>
    public const string OfferLine = "Internet coupé : le cloud ne répond pas. Le cabinet peut continuer sur le PC de secours.";

    /// <summary>
    /// When the PC offers the button after this app lost the cloud: the PC waits for ~20 s of silence on its own
    /// heartbeats (every 10 s), so 25 s covers the usual case. Only a countdown's estimate — the button decides.
    /// </summary>
    public static readonly TimeSpan OfferExpectedAfter = TimeSpan.FromSeconds(25);

    /// <summary>Seconds left on the countdown, in steps of 5 s; 0 once it is due.</summary>
    public static int SecondsLeft(DateTime cloudLostAtUtc, DateTime nowUtc)
    {
        var left = (OfferExpectedAfter - (nowUtc - cloudLostAtUtc)).TotalSeconds;
        return left <= 0 ? 0 : (int)Math.Ceiling(left / 5) * 5;
    }

    /// <summary>
    /// What the unreachable screen says while the PC answers but does not hold (owner, 2026-10-12): not ready, the offer
    /// beside its button, or — until the PC offers it — when it will, never a bare « impossible de joindre ».
    /// </summary>
    public static string WaitingLine(bool? armed, bool canTakeOver, int secondsLeft) =>
        armed == false ? NotReadyLine
        : canTakeOver ? OfferLine
        : secondsLeft > 0 ? $"Internet coupé — le PC de secours sera disponible dans {secondsLeft} secondes."
        : "Internet coupé — le PC de secours sera disponible dans quelques secondes.";

    /// <summary>
    /// Does the cloud answer at all? Asked when the open page says a request got no reply — so a blip on one request
    /// does not throw the window off the page. Any HTTP answer (any status, a certificate this PC does not trust) is
    /// « yes »; no connection within a few seconds is « no ». Never throws.
    /// </summary>
    public static async Task<bool> CloudAnswersAsync(string baseUrl)
    {
        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(baseUrl.TrimEnd('/') + "/api/auth/mode",
                HttpCompletionOption.ResponseHeadersRead);
            return true;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---- where the PC is, kept between runs -----------------------------------------------------------------

    private static string FilePath => Path.Combine(
        ShellProfile.Roaming, "ClinicManagement", "relay-target.json");

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

    /// <summary>The PC's own word, at its last answer, on whether a cut makes it take over. Null: an older PC, or unknown.</summary>
    public static bool? LastArmed { get; private set; }

    /// <summary>The PC offers « Travailler sur le PC de secours » (its last answer).</summary>
    public static bool LastCanTakeOver { get; private set; }

    /// <summary>Said at once when the PC answers « not armed » — no countdown towards a takeover that cannot come.</summary>
    public const string NotReadyLine = "Le PC de secours n'est pas encore prêt : il ne peut pas prendre le relais.";

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
            LastArmed = document.RootElement.TryGetProperty("armed", out var armed) && armed.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? armed.ValueKind == JsonValueKind.True
                : null;
            LastCanTakeOver = document.RootElement.TryGetProperty("canTakeOver", out var offer) && offer.ValueKind == JsonValueKind.True;
            return document.RootElement.TryGetProperty("holding", out var holding) && holding.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>« Travailler sur le PC de secours »: true once the PC holds the cabinet's saves. Never throws.</summary>
    public static async Task<bool> TakeOverAsync(Target target)
    {
        try
        {
            using var client = Client(target);
            client.Timeout = TimeSpan.FromSeconds(15); // the PC waits for its own takeover (≤ 10 s)
            using var body = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{target.Origin}/api/relay/local/take-over", body);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.TryGetProperty("holding", out var holding) && holding.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
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

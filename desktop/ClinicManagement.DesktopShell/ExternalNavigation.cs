using System;
using System.Diagnostics;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// What the shell does with a navigation that is <b>not</b> a page of this clinic's server — the desktop twin of
/// the mobile shells' <c>ExternalNavigation</c> (Android/iOS), which the WPF shell never got.
///
/// <para><b>Why it exists, measured.</b> Without it a top-level navigation to <c>tel:</c> is simply a navigation
/// WebView2 cannot complete, so it lands in <c>NavigationCompleted</c> with <c>IsSuccess == false</c> and
/// <c>WebErrorStatus.ConnectionAborted</c> — indistinguishable, to that handler, from the clinic's server being
/// off. Reported from production on 2026-09-11: tapping a patient's phone number on <c>/patients/{id}</c>
/// replaced the whole application with « Impossible de joindre le serveur du cabinet · Adresse :
/// https://app.apexa.tn:443 · Détail : ConnectionAborted », while that server was answering <c>/health</c> with
/// 200 throughout. One tap on a link that should have dialled a number, and the app was gone.</para>
///
/// <para>⚠️ <b>The scheme list is a WHITELIST, and that is a security decision, not tidiness.</b> The mobile
/// shells can afford « anything that is not http(s) goes to the system » because an Android intent resolves to a
/// handler or fails. Here the hand-off is <c>ShellExecute</c>, which runs things: a page that navigated to
/// <c>file:///C:/…</c> or to some locally-registered scheme would be asking the shell to launch it. Only schemes
/// this product actually emits are handed over (<c>tel:</c> from the patient file and the two rappels cards,
/// <c>mailto:</c> from « Abonnement » and those same cards).</para>
///
/// <para>⚠️ <b>And everything unlisted must be left to the WebView, not refused.</b> <c>blob:</c> and
/// <c>data:</c> are how this app previews a decoded file and hands over a download (<c>lib/download.ts</c>, the
/// HEIC/TIFF/DICOM/mesh decoders), and <c>about:blank</c> is what a popup starts on. Treating « not http(s) » as
/// « give it to Windows » would have broken every one of those to fix <c>tel:</c>.</para>
/// </summary>
public enum NavigationDisposition
{
    /// <summary>A page of the clinic's own server, or a scheme the WebView owns. It loads in place.</summary>
    LoadInShell,

    /// <summary>
    /// Another web origin. The user's real browser gets it and the WebView stays where it is.
    ///
    /// <para>The load-bearing case is « Connecter Google Agenda »: Google <b>refuses to serve its sign-in inside
    /// an embedded browser</b> (<c>disallowed_useragent</c>), so without this the one screen that connects a
    /// clinic's calendar strands the shell on a Google error page at a foreign origin — with no address bar and
    /// no Back button to leave it by. This is the same reason the Android shell reaches for Custom Tabs.</para>
    /// </summary>
    OpenInBrowser,

    /// <summary><c>tel:</c> / <c>mailto:</c> — Windows resolves it; a WebView resolves neither.</summary>
    HandToOperatingSystem,
}

/// <summary>
/// The decision, split from the doing so it can be tested without a window (the shell's own test project has no
/// WebView2 and no message pump).
/// </summary>
public static class ExternalNavigation
{
    /// <summary>
    /// The schemes handed to Windows. See the whitelist note on <see cref="NavigationDisposition"/> before
    /// adding one — each entry is something this shell will ask the OS to launch on a page's say-so.
    /// </summary>
    private static readonly string[] HandedToWindows = ["tel", "mailto"];

    /// <summary>Where <paramref name="uri"/> should be opened, given the server this shell is pointed at.</summary>
    public static NavigationDisposition DispositionFor(string? uri, ServerConfig config)
    {
        // Unparseable, or relative: the WebView's own business. Inventing a hand-off for something we cannot
        // read is how a shell starts launching things it does not understand.
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return NavigationDisposition.LoadInShell;
        }

        var scheme = parsed.Scheme.ToLowerInvariant();

        if (scheme is "http" or "https")
        {
            return IsClinicServer(parsed, config)
                ? NavigationDisposition.LoadInShell
                : NavigationDisposition.OpenInBrowser;
        }

        return Array.IndexOf(HandedToWindows, scheme) >= 0
            ? NavigationDisposition.HandToOperatingSystem
            // blob:, data:, about:, javascript: — the WebView owns these, and this app depends on the first two.
            : NavigationDisposition.LoadInShell;
    }

    /// <summary>
    /// True when <paramref name="uri"/> is a document served by the clinic's own server — the only navigation
    /// whose failure means « the server cannot be reached ».
    ///
    /// <para>⚠️ <c>blob:</c>, <c>data:</c> and <c>about:</c> are <see cref="NavigationDisposition.LoadInShell"/>
    /// too but answer <c>false</c> here: a file preview that fails to decode is not an outage, and the
    /// unreachable panel would take the whole app down over one bad image.</para>
    /// </summary>
    public static bool IsClinicDocument(string? uri, ServerConfig config) =>
        !string.IsNullOrWhiteSpace(uri)
        && Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme.ToLowerInvariant() is "http" or "https"
        && IsClinicServer(parsed, config);

    /// <summary>
    /// True when <paramref name="uri"/> is the configured server.
    ///
    /// <para>⚠️ <b>Host and port, never the host alone.</b> A clinic's own PC serves the app on 5001 and the
    /// hosted deployment on 443, and <c>ServerConfig.BaseUrl</c> always names a port — so comparing hosts only
    /// would call a different service on the same machine « the clinic server ». Compared
    /// case-insensitively: a hostname is not case-sensitive and <c>APP.APEXA.TN</c> is the same server.</para>
    /// </summary>
    private static bool IsClinicServer(Uri uri, ServerConfig config) =>
        string.Equals(uri.Host, config.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == config.Port;

    /// <summary>
    /// Hands <paramref name="uri"/> to Windows. <c>false</c> when nothing on the PC handles it — the caller says
    /// what to do about that.
    ///
    /// <para>⚠️ <c>UseShellExecute</c> is required: without it .NET Core treats the argument as an executable
    /// path and a <c>tel:</c> URI throws <c>Win32Exception</c>. And a PC with no softphone and no mail client
    /// handles neither scheme — a clinic that has never installed one is the normal case, not an error, so a
    /// failure here must not reach the user as a crash.</para>
    /// </summary>
    public static bool Launch(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// How the shell updates itself: silently, in the background, the way every other desktop application does.
///
/// <para>
/// ⚠️ <b>What this replaced was a full-installer download behind a UAC prompt, and that was the wrong model
/// rather than a rough edge.</b> The shell used to be installed by Inno Setup into <c>%ProgramFiles%</c>, and two
/// things follow from that directory automatically: writing to it needs elevation, so every update raised a UAC
/// prompt; and only an installer can write to it, so the update unit was the whole ~50 MB setup. Chrome, VS Code
/// and Slack have neither property, and they avoid both the same way — by installing per-user. Velopack (the
/// maintained successor to Squirrel.Windows) puts the app under <c>%LocalAppData%</c>, ships <b>delta</b>
/// packages, downloads them silently, and applies them with no elevation and nothing to click.
/// </para>
///
/// <para>
/// ⚠️ <b>Nothing here applies an update while the app is running.</b> Downloading is safe at any moment;
/// swapping the files is not, and a « redémarrer maintenant ? » prompt mid-consultation is exactly the
/// interruption this product spends effort avoiding elsewhere. The staged update is applied by
/// <c>SetAutoApplyOnStartup(true)</c> on the next launch — which is what VS Code does, and why nobody notices it
/// happening.
/// </para>
///
/// <para>
/// ⚠️ <b>Every failure is silent and non-fatal.</b> Offline, an older server with no feed, a half-published feed,
/// a corrupt package: all of them mean « not today », never a dialog. The app behind this works, and an update
/// that cannot be fetched is not the user's problem to solve. Velopack verifies each package's checksum itself,
/// so a truncated or substituted download is refused before anything is staged.
/// </para>
/// </summary>
public static class ShellUpdater
{
    /// <summary>
    /// The feed lives on the clinic's own server, under the same host the shell already talks to.
    ///
    /// <para>⚠️ Derived from the configured server address rather than configured separately, because it must be
    /// reachable from wherever the shell is: an offline LAN has no internet, and a hosted cabinet reaches its
    /// server by a public name. The address the shell is already using is the only one it is known to be able to
    /// resolve — the same argument the server makes when it builds this URL from the request it was reached
    /// on.</para>
    /// </summary>
    private const string FeedPath = "/api/meta/client-feed";

    /// <summary>
    /// A newer build, downloaded and waiting. <c>Info</c> is kept because applying it is a SEPARATE act the user
    /// asks for — see <see cref="ApplyAndRestart"/>.
    /// </summary>
    public sealed record Outcome(string StagedVersion, UpdateInfo Info);

    /// <summary>
    /// Checks the server's feed and, if there is a newer build, downloads it and stages it. Returns the version
    /// that will be running after the next launch, or <c>null</c> when there is nothing new.
    ///
    /// <para>⚠️ Returns <c>null</c> for every failure too. The distinction the caller needs is « is there
    /// something to tell the user », and « the feed was unreachable » is not.</para>
    /// </summary>
    public static async Task<Outcome?> CheckAndStageAsync(string serverBaseUrl, IProgress<int>? progress = null)
    {
        try
        {
            // ⚠️ `IsInstalled` is false when running from a plain `dotnet build` output or an old Inno install —
            // both are legitimate states, and neither can be updated in place. Attempting it would throw on every
            // developer machine and on every PC still carrying the pre-Velopack install.
            var manager = new UpdateManager(new SimpleWebSource(serverBaseUrl.TrimEnd('/') + FeedPath));
            if (!manager.IsInstalled)
            {
                return null;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return null; // Already current.
            }

            var version = update.TargetFullRelease.Version.ToString();

            // Velopack keeps what it has already downloaded, so a second pass over the same release is cheap and
            // this is not a guard against wasted work — it is what lets the caller say « prête » rather than
            // « téléchargement » when a previous session already staged it.
            await manager
                .DownloadUpdatesAsync(update, p => progress?.Report(p))
                .ConfigureAwait(false);

            return new Outcome(version, update);
        }
        catch (Exception)
        {
            // Stated on the type: silent, always. There is nothing here a clinic can act on.
            return null;
        }
    }

    /// <summary>
    /// Applies an already-downloaded update and restarts into it. Replaces this process; nothing after it runs.
    ///
    /// <para>⚠️ <b>Only ever called because somebody pressed a button.</b> Downloading in the background asks
    /// nothing of anybody and is safe at any moment; replacing the running application is not the same act, and
    /// deciding it on a user's behalf mid-consultation is the interruption this product avoids everywhere else.
    /// The staged update simply waits until it is convenient.</para>
    /// </summary>
    public static bool ApplyAndRestart(UpdateInfo info)
    {
        try
        {
            var manager = new UpdateManager(new SimpleWebSource("http://localhost/unused"));
            manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The wall's path: fetch the newest release and restart into it immediately.
    ///
    /// <para>⚠️ <b>Restarting is correct here and nowhere else.</b> Below the version floor every <c>/api</c>
    /// call is refused with 426, so there is no work in progress to interrupt — while everywhere else a staged
    /// update waits for the next launch precisely so nobody is interrupted.</para>
    ///
    /// <para>Returns <c>false</c> when there was nothing to fetch or the fetch failed; the caller then says so
    /// and keeps the retry available, because the alternative to a retry on this screen is an operator visit.</para>
    /// </summary>
    public static async Task<bool> DownloadAndRestartAsync(string serverBaseUrl, IProgress<int>? progress = null)
    {
        try
        {
            var manager = new UpdateManager(new SimpleWebSource(serverBaseUrl.TrimEnd('/') + FeedPath));
            if (!manager.IsInstalled)
            {
                return false;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return false;
            }

            await manager.DownloadUpdatesAsync(update, p => progress?.Report(p)).ConfigureAwait(false);

            // Replaces this process. Nothing after it runs.
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

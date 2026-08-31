using System;
using System.IO;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Where this machine's coffre lives — the folder holding the originals of files too large for the server
/// (<c>clinic-file-vault</c>).
///
/// <para>⚠️ <b>It defaults under <c>%ProgramData%</c>, and specifically NOT under the archive-copy folder.</b> The
/// coffre is the <i>primary</i> store of those originals — the app reads from it — so putting it inside the folder
/// the backups land in would make the working copy and its only copy the same disk, which is precisely the failure
/// AC-11 exists to warn about. <c>%ProgramData%</c> rather than <c>%AppData%</c> because it is machine-wide: the
/// dentist and reception may be different Windows accounts on one PC, and a per-user coffre would give them
/// different views of the same patient's imaging.</para>
///
/// <para>⚠️ <b>An unconfigured shell is not a shell without a coffre.</b> This default always resolves, and
/// WebView2 implements <c>showDirectoryPicker</c> besides, so the page's own browser path still works inside this
/// window for a practice that wants the studies somewhere else entirely.</para>
/// </summary>
public static class VaultFolder
{
    /// <summary>The folder the coffre owns. Mirrors the server's <c>VaultPath.RootFolderName</c>.</summary>
    public const string FolderName = "coffre";

    /// <summary>
    /// The coffre's path. Never throws and never creates anything — deciding is separate from acting, because
    /// this is asked on every navigation.
    /// </summary>
    public static string Resolve(ArchiveCopySettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.VaultFolder))
        {
            return settings.VaultFolder.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ClinicManagement",
            FolderName);
    }

    /// <summary>
    /// Makes sure the folder exists and is as protected as its volume allows, and hands back the path — or empty
    /// when it could not be prepared.
    ///
    /// <para>⚠️ <b>Failure is silent and returns empty, never an exception.</b> This runs on the navigation path:
    /// a removable disk that has been unplugged, or a share that is down, must cost the page nothing beyond
    /// « no coffre on this machine », which is a state the app already renders.</para>
    /// </summary>
    public static string Prepare(ArchiveCopySettings settings)
    {
        var path = Resolve(settings);
        if (path.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            Directory.CreateDirectory(path);

            // Best-effort, exactly as the archive folder's own hardening is: a share or an exFAT stick supports
            // no ACLs, and refusing the coffre over a protection that volume could never offer would remove the
            // capability instead of securing it.
            ArchiveCopyService.HardenFolder(path);

            return path;
        }
        catch
        {
            return string.Empty;
        }
    }
}

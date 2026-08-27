namespace ClinicManagement.Infrastructure;

/// <summary>
/// Resolves per-install file locations relative to the application's <b>install directory</b>
/// (<see cref="AppContext.BaseDirectory"/>) rather than the current working directory (R-6, Phase 5).
///
/// When the API runs as a Windows service the working directory is typically <c>C:\Windows\System32</c>,
/// so any path resolved against <see cref="Directory.GetCurrentDirectory"/> (the <c>.local/</c> store,
/// the <c>Files/</c> folder, <c>logs/</c>) would land in the wrong place or fail. Anchoring to the install
/// directory makes these paths stable regardless of how the process was launched.
///
/// Lives in the root <c>ClinicManagement.Infrastructure</c> namespace (like <c>LocalRequest</c> /
/// <c>CorsOrigins</c>) so <c>ClinicManagement.UnitTests</c> can exercise it. Only used by Local-mode code
/// paths — Cloud never resolves <c>.local/</c> or local-disk storage, so Cloud behavior is unchanged.
/// </summary>
public static class LocalInstallPaths
{
    /// <summary>The install directory the process was published/launched from.</summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// Returns <paramref name="pathOrRelative"/> unchanged when it is already absolute; otherwise resolves
    /// it against <see cref="BaseDirectory"/>.
    /// </summary>
    public static string Resolve(string pathOrRelative)
    {
        if (string.IsNullOrWhiteSpace(pathOrRelative))
        {
            throw new ArgumentException("Path must not be null or empty.", nameof(pathOrRelative));
        }

        return Path.IsPathRooted(pathOrRelative)
            ? pathOrRelative
            : Path.GetFullPath(Path.Combine(BaseDirectory, pathOrRelative));
    }

    /// <summary>The gitignored per-install <c>.local/</c> directory (signing key, tokens, certificates).</summary>
    public static string LocalDir => Resolve(".local");

    /// <summary>A file inside the per-install <c>.local/</c> directory.</summary>
    public static string LocalFile(string fileName) => Path.Combine(LocalDir, fileName);

    /// <summary>
    /// The default backup root, and the one path here that is deliberately <b>not</b> install-relative:
    /// <c>%ProgramData%/ClinicManagement/Backups</c> on Windows, the platform's common-application-data folder
    /// elsewhere.
    ///
    /// <para>⚠️ <b>An install-relative <c>Backups/</c> killed every PDF in the process.</b> The folder is
    /// ACL-hardened because it holds patient data, so the app's own account cannot enumerate it — and QuestPDF's
    /// <c>FontManager</c> static constructor walks <see cref="BaseDirectory"/> looking for fonts. Its first walk
    /// threw <c>UnauthorizedAccessException</c>; the CLR caches the resulting <c>TypeInitializationException</c> for
    /// the life of the process, so from then on every document PDF, every background PDF and every emailed
    /// attachment failed with no way back but a restart. <b>Nothing the product writes with restricted permissions
    /// may live under the directory it is loaded from.</b></para>
    ///
    /// <para>It is also where this belongs on its own merits: the install directory is often under
    /// <c>Program Files</c>, which an upgrade replaces. Falls back to install-relative only if the platform reports
    /// no common-data folder at all — a resolvable-but-unwise path still beats an empty one.</para>
    ///
    /// <para>Stated <b>here</b> because two callers resolve it: the backup service and the <c>restore-backup</c>
    /// console verb, whose safety dump has to land in the same place a scheduled backup does.</para>
    /// </summary>
    public static string DefaultBackupRoot
    {
        get
        {
            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            return string.IsNullOrWhiteSpace(commonData)
                ? Resolve(BackupFolderName)
                : Path.Combine(commonData, DataFolderName, BackupFolderName);
        }
    }

    /// <summary>The per-machine data folder <see cref="DefaultBackupRoot"/> hangs under.</summary>
    private const string DataFolderName = "ClinicManagement";

    /// <summary>The leaf folder name, shared so the two resolvers cannot name it differently.</summary>
    private const string BackupFolderName = "Backups";
}

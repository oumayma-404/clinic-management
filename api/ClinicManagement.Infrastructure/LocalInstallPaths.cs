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
}

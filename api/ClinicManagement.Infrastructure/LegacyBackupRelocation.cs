namespace ClinicManagement.Infrastructure;

/// <summary>
/// Moves a pre-existing <c>Backups/</c> folder out of the install directory at startup — the half of the
/// PDF fix that <see cref="LocalInstallPaths.DefaultBackupRoot"/> could not do on its own.
///
/// <para>⚠️ <b>Changing where new backups are written does not repair an install that already has one.</b>
/// <c>DefaultBackupRoot</c> stops the product creating an ACL-hardened folder under
/// <see cref="AppContext.BaseDirectory"/>, but QuestPDF's <c>FontManager</c> static constructor walks that
/// directory <b>recursively</b> looking for fonts, and one unreadable subdirectory anywhere beneath it throws
/// <c>UnauthorizedAccessException</c>. The CLR caches the resulting <c>TypeInitializationException</c> for the
/// life of the process, so every ordonnance, every background PDF and every emailed attachment fails until a
/// restart that changes nothing. Measured on a dev machine carrying three such folders: every
/// <c>generate-pdf-download</c> returned 400; with the folder moved out, the same request returned a
/// 49 627-byte <c>%PDF-</c>. So an upgraded clinic — one that ran a backup on the old default — keeps the dead
/// renderer until the folder physically leaves the tree.</para>
///
/// <para>⚠️ <b>Renaming it is not enough.</b> The scan is of the whole tree, not of a folder called
/// <c>Backups</c>: a rename to <c>Backups.disabled</c> left the failure exactly as it was. It has to move
/// <b>out</b> of <see cref="AppContext.BaseDirectory"/>.</para>
///
/// <para>The folder is moved whole, never enumerated. Its children are the unreadable part — listing them would
/// throw the very exception this exists to avoid — while a directory move only needs write access on the
/// parent, which the app has.</para>
/// </summary>
public static class LegacyBackupRelocation
{
    /// <summary>Name of the folder the old default created under the install directory.</summary>
    private const string LegacyFolderName = "Backups";

    /// <summary>Where the moved folder is parked under the new root, so it stays visible as backup data.</summary>
    private const string RelocatedFolderName = "legacy-install-dir";

    /// <summary>
    /// Relocates the legacy folder if it is present, and reports what happened in one line for the log.
    ///
    /// <para><b>Never throws.</b> This runs before the host is built, and a clinic that cannot move the folder
    /// must still start — it loses PDFs, which is what it already had, not its server. The returned string is the
    /// message to log; <c>null</c> means there was nothing to do.</para>
    /// </summary>
    public static string? Relocate()
    {
        try
        {
            var legacy = Path.Combine(LocalInstallPaths.BaseDirectory, LegacyFolderName);
            if (!Directory.Exists(legacy))
            {
                return null;
            }

            // The install-relative path IS the new root only when the platform reports no common-data folder.
            // Moving it onto itself would be both wrong and destructive.
            var newRoot = LocalInstallPaths.DefaultBackupRoot;
            if (PathsAreSame(legacy, newRoot))
            {
                return $"Le dossier de sauvegarde est le dossier d'installation ({legacy}) : "
                     + "impossible de le déplacer, la génération de PDF restera indisponible.";
            }

            var destination = FreeDestination(Path.Combine(newRoot, RelocatedFolderName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(legacy, destination);

            return $"Sauvegardes héritées déplacées de {legacy} vers {destination} — "
                 + "elles bloquaient la génération de PDF depuis le dossier d'installation.";
        }
        catch (Exception ex)
        {
            return $"Les sauvegardes héritées n'ont pas pu être déplacées hors du dossier d'installation "
                 + $"({ex.GetType().Name}: {ex.Message}). La génération de PDF peut rester indisponible.";
        }
    }

    /// <summary>First unused name, so a second upgrade cannot overwrite what the first one parked.</summary>
    private static string FreeDestination(string preferred)
    {
        if (!Directory.Exists(preferred) && !File.Exists(preferred))
        {
            return preferred;
        }

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{preferred}-{n}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"No free destination beside {preferred}.");
    }

    private static bool PathsAreSame(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

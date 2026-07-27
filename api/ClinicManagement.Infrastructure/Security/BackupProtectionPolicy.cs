namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Decides whether a backup destination can actually be protected by NTFS permissions.
///
/// A backup is a full <c>pg_dump</c> of every patient record plus a recursive copy of the entire file store,
/// so it deserves the same posture as the live data (<see cref="DirectoryAclHardener"/>). But the admin picks
/// the destination, and on a USB stick (often FAT32, which has no ACLs at all) or a network share (where the
/// ACL is the server's to enforce, not ours) a "secured" folder would be a false promise.
///
/// So: protect what can be protected, and tell the admin plainly when it cannot be. Refusing the backup
/// outright would be worse — an operator who cannot take a backup to a USB drive simply stops taking backups.
/// </summary>
public static class BackupProtectionPolicy
{
    /// <summary>
    /// Shown to the admin — and returned from the API — when the destination cannot be locked down. Says what
    /// is actually at risk rather than a vague "could not set permissions".
    /// </summary>
    public const string UnprotectableDestinationWarning =
        "La sauvegarde a bien été créée, mais elle est enregistrée sur un support amovible ou réseau où les " +
        "droits d'accès ne peuvent pas être garantis : toute personne ayant accès à ce support peut lire les " +
        "dossiers patients. Conservez-la en lieu sûr.";

    /// <summary>
    /// True only for a local fixed disk. Removable, network, CD and unknown drive types cannot be relied on
    /// to honour an ACL, so the caller warns instead of pretending the backup is protected.
    /// </summary>
    public static bool CanProtect(DriveType driveType) => driveType == DriveType.Fixed;

    /// <summary>
    /// Best-effort drive type for <paramref name="path"/>. Anything unresolvable reads as
    /// <see cref="DriveType.Unknown"/>, which <see cref="CanProtect"/> treats as "cannot protect" — the safe
    /// direction: warn the admin rather than claim a protection we did not verify.
    /// </summary>
    public static DriveType ResolveDriveType(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return DriveType.Unknown;
            }

            // A UNC path (\\server\share) has no DriveInfo — it is a network destination by definition.
            if (root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return DriveType.Network;
            }

            return new DriveInfo(root).DriveType;
        }
        catch
        {
            return DriveType.Unknown;
        }
    }
}

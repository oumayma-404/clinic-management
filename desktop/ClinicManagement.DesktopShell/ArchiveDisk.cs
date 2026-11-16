using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClinicManagement.DesktopShell.Camera;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Whether the archive folder is on the archive disk right now (<c>camera-photo-import</c>).
///
/// <para>⚠️ <b>A drive letter is not a disk.</b> With the archive disk unplugged, the next volume to arrive takes its
/// letter — and from 1.8 that is very often the camera's card, read on this PC every day. A copy to « E:\Sauvegardes »
/// then writes the cabinet's record onto the card that leaves in the camera bag, and calls the copy done.</para>
///
/// <para>⚠️ <b>Two rules, two reaches.</b> <see cref="Unplugged"/> (no volume there, or a camera's card) holds every
/// copy into the folder — the archive, the coffre, the file mirror and the photo originals — because it cannot be
/// wrong. <see cref="Refusal"/> adds the disk's identity and holds the photo originals only: the three older copies
/// worked onto whichever disk carried the folder, and a cabinet using two disks in turn must not lose them.</para>
/// </summary>
public static class ArchiveDisk
{
    /// <summary>A French sentence when nothing, or a camera's card, sits where the archive folder lives; else null.</summary>
    public static string? Unplugged(string folder, Func<string, bool>? isCameraCard = null)
    {
        var root = RootOf(folder);
        if (root is null)
        {
            return null;
        }

        if (!Directory.Exists(root))
        {
            return $"Le disque de copie n'est pas branché ({Name(root)} est introuvable) : rien n'a été copié.";
        }

        return (isCameraCard ?? CameraSources.IsCard)(root)
            // The DCIM folder is named: a USB stick holding one is read as a card, and that cabinet must see why.
            ? $"Le disque de copie n'est pas branché : {Name(root)} est la carte d'un appareil photo (un dossier DCIM "
              + "à sa racine), rien n'y a été copié."
            : null;
    }

    /// <summary>
    /// <see cref="Unplugged"/>, then the disk's identity: refused when <paramref name="knownVolumes"/> names the archive
    /// disks and the volume there is none of them.
    ///
    /// <para>⚠️ <b>No disk recorded yet</b> (settings saved before 1.8): the folder itself must already be there — the
    /// archive copy made it on the archive disk — so the first copy never creates the tree on a stranger. The caller
    /// then records that disk (<see cref="ArchiveCopySettingsStore.RememberFolderVolume"/>).</para>
    /// </summary>
    public static string? Refusal(
        string folder,
        IReadOnlyCollection<uint> knownVolumes,
        Func<string, uint?>? volumeOf = null,
        Func<string, bool>? isCameraCard = null)
    {
        if (Unplugged(folder, isCameraCard) is { } unplugged)
        {
            return unplugged;
        }

        var root = RootOf(folder);
        if (root is null)
        {
            return null;
        }

        if (knownVolumes.Count == 0)
        {
            return Directory.Exists(folder)
                ? null
                : $"Le disque de copie n'est pas branché ({folder} est introuvable) : rien n'a été copié.";
        }

        // A volume Windows will not describe (a share) is not called a stranger: « je ne sais pas » is not a refusal.
        return (volumeOf ?? PhysicalDisks.VolumeSerial)(folder) is { } current && !knownVolumes.Contains(current)
            ? $"Le disque de copie n'est pas branché : {Name(root)} est un autre disque, rien n'y a été copié. "
              + "Si c'est votre nouveau disque de copie, enregistrez-le dans « Copie automatique de l'archive… »."
            : null;
    }

    /// <summary>
    /// The serial to remember as an archive disk for <paramref name="folder"/> now, or null when there is nothing to
    /// trust: a share (no letter to lose, and asking it may hang), nothing plugged in, or a camera's card.
    /// </summary>
    public static uint? IdentityOf(string folder) =>
        string.IsNullOrWhiteSpace(folder) || folder.Trim().StartsWith(@"\\", StringComparison.Ordinal)
        || Unplugged(folder) is not null
            ? null
            : PhysicalDisks.VolumeSerial(folder);

    private static string? RootOf(string folder)
    {
        try
        {
            return string.IsNullOrWhiteSpace(folder) ? null : Path.GetPathRoot(Path.GetFullPath(folder));
        }
        catch
        {
            return null;
        }
    }

    private static string Name(string root) => root.Length == 3 && root[1] == ':' ? root[..2] : root;
}

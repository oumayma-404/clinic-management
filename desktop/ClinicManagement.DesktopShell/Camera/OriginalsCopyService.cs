using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// How a copy of the originals to the archive disk went. <see cref="Complete"/> is the only thing that may tell the
/// server « covered »; <see cref="Files"/>/<see cref="Bytes"/> are what the destination now holds of the originals.
/// </summary>
public sealed record OriginalsCopyOutcome(bool Complete, string Sentence, int Files, long Bytes, string HeldBy);

/// <summary>
/// Copies the originals this PC keeps onto the cabinet's archive disk (<c>features/camera-photo-import</c>, phase 2):
/// <c>{dossier de copie}\photos-originales\</c>, add-only, beside the coffre's own copy.
///
/// <para>⚠️ <b>The page reports, not the shell.</b> The server checks the report against the originals it has on
/// record for this PC and only then advances the stamp the 7-day alert reads; the page is where the cabinet's
/// session is, and the shell's only credential (the archive grant) is refused outside the routes made for it.</para>
///
/// <para>⚠️ <b>Same physical disk = refused</b>, and so is any disk that is not the archive disk
/// (<see cref="ArchiveDisk.Refusal"/>): this copy runs 30 s after a photo is kept, i.e. while the camera's card is
/// in the reader — the volume most likely to have taken the archive disk's letter.</para>
/// </summary>
public static class OriginalsCopyService
{
    public const string DestinationFolderName = "photos-originales";

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int RaisedByThisApp = unchecked((int)0x80131620); // COR_E_IO: an IOException built from a sentence

    /// <param name="diskRefusal">Whether the folder is on the archive disk right now; <see cref="ArchiveDisk.Refusal"/>
    /// with no disk recorded when not given.</param>
    public static async Task<OriginalsCopyOutcome> CopyNowAsync(
        OriginalsStore store,
        string archiveFolder,
        Func<string, string?>? diskRefusal = null,
        Func<string, string, string?>? sameDiskRefusal = null,
        CancellationToken cancellationToken = default)
    {
        var heldBy = Environment.MachineName;

        if (string.IsNullOrWhiteSpace(archiveFolder))
        {
            return new OriginalsCopyOutcome(
                false,
                "Aucun dossier de copie n'est choisi sur ce PC : les photos originales n'ont qu'un exemplaire.",
                0, 0, heldBy);
        }

        if (!Directory.Exists(store.Root))
        {
            // Nothing kept is nothing to protect: an empty store is fully copied.
            return new OriginalsCopyOutcome(true, "Aucune photo originale n'est gardée sur ce PC.", 0, 0, heldBy);
        }

        // ⚠️ Before anything is created there: the index names every patient.
        var refusal = (diskRefusal ?? (folder => ArchiveDisk.Refusal(folder, Array.Empty<uint>())))(archiveFolder)
                      ?? (sameDiskRefusal ?? AddOnlyFolderCopy.SameDiskRefusal)(store.Root, archiveFolder);
        if (refusal != null)
        {
            return new OriginalsCopyOutcome(false, refusal, 0, 0, heldBy);
        }

        var destination = Path.Combine(archiveFolder, DestinationFolderName);
        AddOnlyFolderCopy.Result originals;
        try
        {
            Directory.CreateDirectory(destination);
            ArchiveCopyService.HardenFolder(destination);

            originals = await AddOnlyFolderCopy.CopyAsync(
                store.Root, destination, OriginalsPathPlanner.IsOriginal, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OriginalsCopyOutcome(
                false, $"La copie des photos originales a échoué : {Reason(ex)}.", 0, 0, heldBy);
        }

        var sentence = originals.Copied == 0
            ? $"Photos originales déjà à jour sur le disque de copie ({originals.Files})."
            : $"{originals.Copied} photo(s) originale(s) copiée(s) sur le disque de copie.";

        // ⚠️ The index after the originals, and never instead of them: a sheet left open in a spreadsheet must not
        // turn a copy whose every original arrived into « échec » — and the 7-day alert with it.
        try
        {
            store.FlushIndex();
            await AddOnlyFolderCopy.CopyAsync(
                store.Root,
                destination,
                relative => string.Equals(relative, OriginalsPathPlanner.IndexFileName, StringComparison.OrdinalIgnoreCase),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            sentence += " La liste index.csv n'a pas pu être mise à jour (ouverte dans un autre programme ?) : "
                        + "elle le sera à la prochaine copie.";
        }

        return new OriginalsCopyOutcome(true, sentence, originals.Files, originals.Bytes, heldBy);
    }

    /// <summary>Why a copy failed, in French: Windows' own messages reach .NET in English.</summary>
    private static string Reason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "accès refusé au dossier de copie",
        DirectoryNotFoundException or DriveNotFoundException => "le disque de copie a été débranché",
        IOException { HResult: SharingViolation or LockViolation } => "un fichier est ouvert dans un autre programme",
        IOException { HResult: DiskFull } => "le disque de copie est plein",

        // AddOnlyFolderCopy's own « espace insuffisant sur E:\ (…) » is already French.
        IOException { HResult: RaisedByThisApp } => ex.Message,
        _ => "erreur de lecture ou d'écriture sur le disque de copie",
    };
}

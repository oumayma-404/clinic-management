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
/// <para>⚠️ <b>Same physical disk = refused</b>, the coffre's rule: a copy that dies with the original is reported
/// as no copy, so the alert keeps saying what is true.</para>
/// </summary>
public static class OriginalsCopyService
{
    public const string DestinationFolderName = "photos-originales";

    public static async Task<OriginalsCopyOutcome> CopyNowAsync(
        OriginalsStore store,
        string archiveFolder,
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

        var refusal = (sameDiskRefusal ?? AddOnlyFolderCopy.SameDiskRefusal)(store.Root, archiveFolder);
        if (refusal != null)
        {
            return new OriginalsCopyOutcome(false, refusal, 0, 0, heldBy);
        }

        var destination = Path.Combine(archiveFolder, DestinationFolderName);
        try
        {
            Directory.CreateDirectory(destination);
            ArchiveCopyService.HardenFolder(destination);

            // The originals first, counted exactly; then the index, which only grows (a new size is a new copy).
            var originals = await AddOnlyFolderCopy.CopyAsync(
                store.Root, destination, OriginalsPathPlanner.IsOriginal, cancellationToken);
            await AddOnlyFolderCopy.CopyAsync(
                store.Root,
                destination,
                relative => string.Equals(relative, OriginalsPathPlanner.IndexFileName, StringComparison.OrdinalIgnoreCase),
                cancellationToken);

            var sentence = originals.Copied == 0
                ? $"Photos originales déjà à jour sur le disque de copie ({originals.Files})."
                : $"{originals.Copied} photo(s) originale(s) copiée(s) sur le disque de copie.";
            return new OriginalsCopyOutcome(true, sentence, originals.Files, originals.Bytes, heldBy);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OriginalsCopyOutcome(
                false, $"La copie des photos originales a échoué : {ex.Message}", 0, 0, heldBy);
        }
    }
}

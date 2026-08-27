using System.IO.Compression;

namespace ClinicManagement.Application.Features.Backup.Archive;

/// <summary>
/// What an uploaded archive may cost to <b>open</b>, as opposed to how large the upload itself is
/// (<c>clinic-data-archive-and-restore</c>).
///
/// <para><b>Why the upload's own size is not the bound.</b> The controller caps the compressed bytes, and deflate
/// reaches ratios around 1000:1 on the repetitive JSON this archive is made of — so a one-megabyte file passes
/// every gate the transport has and still decompresses into gigabytes. The restore then read each
/// <c>data/*.json</c> entry to the end of a single UTF-16 <c>string</c> (~2× the bytes) before a row was parsed,
/// and deserialized it into a second copy. On the hosted deployment one process serves every practice, so that is
/// one crafted upload against every cabinet at once — by any clinic admin, on an endpoint that deliberately keeps
/// working for an expired cabinet.</para>
///
/// <para>⚠️ <b>Both terms are needed and neither is sufficient.</b> A total budget alone admits an archive of ten
/// thousand modest entries; a ratio alone admits a genuinely enormous but honestly-compressed file. The figures
/// are deliberately far above any real cabinet — twenty years of a busy practice's rows are megabytes, and the
/// radiographs that dominate an archive are already-compressed formats that barely deflate at all, so a legitimate
/// file has a ratio near 1.</para>
///
/// <para>⚠️ <b>Read before anything else, on the manifest path both doors share.</b> A check the console door
/// could skip would be a check that does not exist, since that door is the one reached when nobody at the practice
/// can act.</para>
/// </summary>
public static class ClinicArchiveLimits
{
    /// <summary>How much an archive may decompress to in total. Two orders of magnitude above a real cabinet.</summary>
    public const long MaxUncompressedBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// How many times an entry may exceed its own compressed size. Text deflates ~5:1 and an image barely at all;
    /// 200:1 is reached by padding and by nothing a practice produces.
    /// </summary>
    public const int MaxCompressionRatio = 200;

    /// <summary>How many entries an archive may hold — one per table plus one per blob, with room to spare.</summary>
    public const int MaxEntries = 200_000;

    /// <summary>
    /// The French refusal this archive earns, or null when it is safe to open.
    ///
    /// <para>It reads <see cref="ZipArchiveEntry.Length"/>, which comes from the central directory rather than
    /// from decompressing — a crafted header only ever <i>over</i>-states, and an under-stated one is caught by
    /// the ratio term on the same entry.</para>
    /// </summary>
    public static string? Refuse(ZipArchive zip)
    {
        if (zip.Entries.Count > MaxEntries)
        {
            return "Cette archive contient trop de fichiers pour être ouverte.";
        }

        long total = 0;

        foreach (var entry in zip.Entries)
        {
            total += entry.Length;

            if (total > MaxUncompressedBytes)
            {
                return "Cette archive est trop volumineuse une fois décompressée pour être restaurée.";
            }

            if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
            {
                return "Cette archive ne peut pas être ouverte : l'un de ses fichiers est anormalement compressé.";
            }
        }

        return null;
    }
}

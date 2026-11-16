using System;
using System.Collections.Generic;
using System.IO;

namespace ClinicManagement.DesktopShell.Camera;

public enum CameraFileKind
{
    Jpeg,
    Raw,
    Heic,
    Video,
    Other,
}

/// <summary>
/// What a file on a camera or a card is (<c>features/camera-photo-import</c>, phase 2): only a JPEG is offered for
/// filing, the rest is counted so « 37 RAW ignorées » can say what to change on the camera.
///
/// <para>⚠️ <b>A mirror of <c>web/lib/photo-import/sources.ts</c></b>, which classifies the browser path's files. A
/// format one side calls JPEG and the other does not would be copied and never read, or never copied at all —
/// silently. <c>CameraFileKindsContractTests</c> parses the web file and holds the four sets equal.</para>
///
/// <para>⚠️ <b>Not a leading <c>_</c></b>: a Nikon in Adobe RGB names every photo <c>_DSC0001.JPG</c>.</para>
/// </summary>
public static class CameraFileKinds
{
    public static readonly IReadOnlySet<string> Jpeg = Set("jpg", "jpeg", "jpe");

    public static readonly IReadOnlySet<string> Raw = Set(
        "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "dng", "raf", "orf", "rw2", "pef", "srw", "x3f", "3fr",
        "iiq", "rwl");

    public static readonly IReadOnlySet<string> Heic = Set("heic", "heif", "hif");

    public static readonly IReadOnlySet<string> Video = Set(
        "mp4", "mov", "avi", "mts", "m2ts", "mpg", "mpeg", "3gp", "wmv", "mkv");

    public static CameraFileKind Of(string name)
    {
        var extension = Path.GetExtension(name).TrimStart('.');
        if (Jpeg.Contains(extension))
        {
            return CameraFileKind.Jpeg;
        }

        if (Raw.Contains(extension))
        {
            return CameraFileKind.Raw;
        }

        if (Heic.Contains(extension))
        {
            return CameraFileKind.Heic;
        }

        return Video.Contains(extension) ? CameraFileKind.Video : CameraFileKind.Other;
    }

    /// <summary>
    /// A card's own bookkeeping, never a photo — skipped in silence, because a count nobody can act on (« 1 autre
    /// ignoré » on every Canon card) is noise: Canon's folder catalogue (<c>M0100.CTG</c>), Nikon's card marker
    /// (<c>NIKON001.DSC</c>), a video's thumbnail (<c>.THM</c>), a GoPro's low-resolution video, an editor's sidecar,
    /// a print order. Mirrors <c>sources.ts</c>; <c>CameraContractTests</c> holds the three sets equal.
    /// </summary>
    public static readonly IReadOnlySet<string> IgnoredNames = Set("thumbs.db", "desktop.ini");

    public static readonly IReadOnlySet<string> IgnoredExtensions = Set("ctg", "dsc", "thm", "lrv", "xmp", "mrk");

    /// <summary>
    /// <c>CANONMSC</c> / <c>EOSMISC</c> (Canon), <c>PRIVATE</c> (a Sony's video tree — its <c>THMBNL</c> holds a JPEG
    /// still of every clip, which is not a photo), <c>AVF_INFO</c> (a Sony's index), <c>MISC</c> and the system's own.
    /// </summary>
    public static readonly IReadOnlySet<string> IgnoredFolders = Set(
        "misc", "$recycle.bin", "system volume information", "canonmsc", "eosmisc", "private", "avf_info");

    /// <summary>Hidden and AppleDouble files (a leading dot), thumbnail databases, the bookkeeping extensions.</summary>
    public static bool IsIgnoredName(string name) =>
        name.StartsWith('.')
        || IgnoredNames.Contains(name)
        || IgnoredExtensions.Contains(Path.GetExtension(name).TrimStart('.'));

    public static bool IsIgnoredFolder(string name) =>
        name.StartsWith('.') || IgnoredFolders.Contains(name);

    /// <summary>
    /// « DCIM\100CANON\IMG_0412 » — one shot whatever its extension, so a RAW can be told from the JPEG beside it
    /// (<see cref="CameraBatchPlanner"/>).
    /// </summary>
    public static string ShotKey(string objectId)
    {
        var normalized = objectId.Replace('/', '\\');
        var folder = Path.GetDirectoryName(normalized) ?? string.Empty;
        return Path.Combine(folder, Path.GetFileNameWithoutExtension(normalized)).ToLowerInvariant();
    }

    private static IReadOnlySet<string> Set(params string[] extensions) =>
        new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
}

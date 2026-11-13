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

    /// <summary>A card's own bookkeeping, never a photo: hidden and AppleDouble files, thumbnail databases.</summary>
    public static bool IsIgnoredName(string name) =>
        name.StartsWith('.')
        || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    public static bool IsIgnoredFolder(string name) =>
        name.StartsWith('.')
        || name.Equals("misc", StringComparison.OrdinalIgnoreCase)
        || name.Equals("$recycle.bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("system volume information", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Set(params string[] extensions) =>
        new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
}

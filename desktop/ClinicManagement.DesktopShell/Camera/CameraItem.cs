using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// One file a camera or a card holds, as listed — never opened yet.
///
/// <para><see cref="ObjectId"/> is the item's path inside its source (« DCIM\100CANON\IMG_0001.JPG »), stable for as
/// long as the card is not reformatted. <see cref="TakenLocal"/> is the file's own date on the PC's wall clock: a
/// camera writes a photo at the moment it takes it, so it is the capture time to within a second — good enough to
/// decide what is new, never used to match a patient (the page reads EXIF for that).</para>
/// </summary>
public sealed record CameraItem(string ObjectId, string Name, long Size, DateTime? TakenLocal, CameraFileKind Kind);

/// <summary>
/// Somewhere photos come from: a card in a reader, or a camera on a USB cable.
///
/// <para>⚠️ <b>Read-only, always.</b> Nothing here writes to, renames or deletes anything on the source — a card
/// emptied by an import tool is a dentist's photos gone.</para>
/// </summary>
public interface ICameraSource
{
    /// <summary>Stable per card or device: the journal remembers what was already filed from it.</summary>
    string SourceKey { get; }

    /// <summary>What the dentist reads: « Carte SD (E:) », « Canon EOS 250D ».</summary>
    string Label { get; }

    Task<IReadOnlyList<CameraItem>> ListAsync(CancellationToken cancellationToken);

    Task CopyToAsync(CameraItem item, Stream destination, CancellationToken cancellationToken);
}

/// <summary>What a capped listing keeps — the card and the cable path share it.</summary>
public static class CameraListing
{
    /// <summary>
    /// The newest <paramref name="max"/>: by date, then by path, which grows with time on a camera (DCF numbers its
    /// folders and files). ⚠️ A cap applied in listing order kept the OLDEST — a card's directory order is the order
    /// the photos were written — so on a full card today's photos were never offered at all.
    /// </summary>
    public static IReadOnlyList<T> KeepNewest<T>(
        IReadOnlyCollection<T> items, Func<T, DateTime?> dateOf, Func<T, string> pathOf, int max) =>
        items.Count <= max
            ? items.ToList()
            : items
                .OrderByDescending(item => dateOf(item) ?? DateTime.MinValue)
                .ThenByDescending(pathOf, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToList();
}

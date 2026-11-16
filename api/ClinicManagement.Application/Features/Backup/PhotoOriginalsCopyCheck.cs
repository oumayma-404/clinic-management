using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Backup;

/// <summary>
/// « Are the camera originals safe? », asked per clinic PC (<c>features/camera-photo-import</c>). One rule for the two
/// places that ask it — the nightly pass and a PC's own copy report — so the bell cannot say one thing at night and
/// another the moment the PC reports.
///
/// <para>A PC is stale when it keeps originals its last <b>complete</b> copy did not cover, and the oldest of them has
/// waited <see cref="PhotoOriginalsCopy.StaleAfterDays"/> days. A PC that copied yesterday and kept a photo this
/// morning is not stale: that photo is hours old, and the next copy is minutes away on any PC with the disk plugged
/// in.</para>
/// </summary>
public static class PhotoOriginalsCopyCheck
{
    public static async Task<IReadOnlyList<StalePhotoOriginals>> FindStaleAsync(
        IPatientFileRepository files,
        IPhotoOriginalsCopyRepository copies,
        Guid clinicId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var holders = await files.GetCameraOriginalTotalsAsync(clinicId, cancellationToken);
        if (holders.Count == 0)
        {
            return Array.Empty<StalePhotoOriginals>();
        }

        var reports = (await copies.ListAsync(clinicId, cancellationToken))
            .ToDictionary(c => c.HeldBy, StringComparer.Ordinal);
        var threshold = nowUtc.AddDays(-PhotoOriginalsCopy.StaleAfterDays);

        var stale = new List<StalePhotoOriginals>();
        foreach (var holder in holders)
        {
            var lastComplete = reports.TryGetValue(holder.HeldBy, out var report) ? report.LastCompleteCopyAtUtc : null;
            var uncovered = await files.GetUncoveredOriginalsAsync(clinicId, holder.HeldBy, lastComplete, cancellationToken);
            if (uncovered.Files > 0 && uncovered.OldestKeptAtUtc is { } oldest && oldest < threshold)
            {
                stale.Add(new StalePhotoOriginals(holder.HeldBy, uncovered.Files, oldest));
            }
        }

        return stale;
    }

    /// <summary>Raises, restates or clears the cabinet's one alert to match <paramref name="stale"/>.</summary>
    public static Task ApplyAsync(
        INotificationGenerator notifications,
        Guid clinicId,
        IReadOnlyList<StalePhotoOriginals> stale,
        CancellationToken cancellationToken = default) =>
        stale.Count == 0
            ? notifications.ClearPhotoOriginalsCopyStaleAsync(clinicId, cancellationToken)
            : notifications.EnsurePhotoOriginalsCopyStaleAsync(clinicId, stale, cancellationToken);
}

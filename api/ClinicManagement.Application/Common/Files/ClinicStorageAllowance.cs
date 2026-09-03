using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Files;

/// <summary>What a cabinet is using of the space it has, and whether that space is bounded at all.</summary>
/// <param name="UsedBytes">Hosted patient files belonging to this cabinet. See the ⚠️ on what this excludes.</param>
/// <param name="QuotaBytes">The ceiling, or 0 where nothing is enforced.</param>
/// <param name="Enforced">False where the cabinet owns the disk — there is nobody to protect it from.</param>
public readonly record struct ClinicStorageUsage(long UsedBytes, long QuotaBytes, bool Enforced)
{
    public static readonly ClinicStorageUsage Unbounded = new(0, 0, false);

    public long RemainingBytes => Enforced ? Math.Max(0, QuotaBytes - UsedBytes) : long.MaxValue;
}

/// <summary>
/// How much of a cabinet's storage is spoken for, and whether one more file fits (<c>large-file-transfer</c>
/// Part 4).
///
/// <para>⚠️ <b>It exists because Part 3 raised the per-file line six-fold and nothing counted the total.</b>
/// Making a study hostable removed the reason a clinic could not fill the deployment's disk; a per-file cap
/// bounds one upload and says nothing about ten thousand. On a hosted multi-tenant box a full disk is not a
/// billing problem — it stops every cabinet at once.</para>
///
/// <para>⚠️ <b>What it counts is hosted PATIENT FILES, and that is a deliberate line rather than everything on
/// the disk.</b> Medical-document PDFs, cachets, clinic logos and preview images also occupy space, and they are
/// each a few hundred kilobytes against a study's hundred megabytes — the term that grows is the one measured.
/// Recovery points are excluded for a different and stronger reason: they are the <i>vendor's</i> copies of this
/// cabinet's records, so charging them to the cabinet's ceiling would push a practice over a limit by an act it
/// did not perform and cannot undo.</para>
///
/// <para>⚠️ It is therefore a <b>close under-estimate, never an over-estimate</b>, which is the safe direction
/// for a figure a user is shown: a cabinet is never told it is fuller than it is.</para>
/// </summary>
public class ClinicStorageAllowance
{
    private readonly IPatientFileRepository _files;
    private readonly IClinicStoragePolicy _policy;

    public ClinicStorageAllowance(IPatientFileRepository files, IClinicStoragePolicy policy)
    {
        _files = files;
        _policy = policy;
    }

    /// <summary>
    /// Where the cabinet stands. ⚠️ Answers <see cref="ClinicStorageUsage.Unbounded"/> without reading anything
    /// when the deployment does not enforce a quota — on <c>SelfHostedLan</c> the clinic's own machine is the
    /// object store, so the figure would be a number about their own disk that this product does not manage.
    /// </summary>
    public async Task<ClinicStorageUsage> ReadAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        if (!_policy.Enforced)
        {
            return ClinicStorageUsage.Unbounded;
        }

        var used = await _files.GetHostedBytesAsync(clinicId, cancellationToken);
        return new ClinicStorageUsage(used, _policy.QuotaBytes, true);
    }

    /// <summary>
    /// Whether one more file of <paramref name="incomingBytes"/> fits, as a refusal a user can act on.
    ///
    /// <para>⚠️ Asked <b>before</b> the bytes are sent wherever a door can — a resumable upload opens with a
    /// declared length precisely so a refusal costs nothing, and « you are out of space » discovered after four
    /// minutes of a clinic's uplink is the failure that part exists to end.</para>
    ///
    /// <para>⚠️ It compares <c>used + incoming</c> against the ceiling rather than <c>used</c> alone, so the
    /// last file that fits is accepted and the first that does not is refused — a check on `used` only would let
    /// a cabinet one byte under its limit store another hundred and fifty megabytes.</para>
    /// </summary>
    public async Task<Result> EnsureRoomForAsync(
        Guid clinicId, long incomingBytes, CancellationToken cancellationToken = default)
    {
        var usage = await ReadAsync(clinicId, cancellationToken);
        if (!usage.Enforced || usage.UsedBytes + incomingBytes <= usage.QuotaBytes)
        {
            return Result.Success();
        }

        return Result.Failure(
            ClinicStorageRefusals.Full(usage.UsedBytes, usage.QuotaBytes),
            ClinicStorageRefusals.FullCode);
    }
}

/// <summary>
/// The French a storage refusal is expressed in, beside <c>FileResidencyRefusals</c> and on its terms: the
/// sentence and the code live together, and the sentence says what still works before what does not.
/// </summary>
public static class ClinicStorageRefusals
{
    /// <summary>Branched on by the picker so « plein » is not rendered as a generic upload failure.</summary>
    public const string FullCode = "storage_full";

    public static string Full(long usedBytes, long quotaBytes) =>
        $"L'espace de stockage du cabinet est plein ({Size(usedBytes)} sur {Size(quotaBytes)} utilisés). "
        + "Les fichiers déjà envoyés restent accessibles. Supprimez des fichiers volumineux, ou contactez APEXA "
        + "pour augmenter l'espace.";

    /// <summary>
    /// A size a dentist can read, mirroring the browser's own <c>formatFileSize</c>.
    ///
    /// <para>⚠️ <b>The unit adapts, and forcing gigabytes was a real defect</b> — caught by reading the served
    /// sentence rather than by any test: a cabinet using 7,4 Mo of a 60 Mo ceiling was told « 0,0 Go sur 0,1 Go
    /// utilisés », which states nothing and reads as a broken figure. Below a gigabyte the useful unit is the
    /// megabyte.</para>
    ///
    /// <para>⚠️ A comma, always — this product prints every size and every dinar in French, and « 12.4 GB » in
    /// the middle of a French sentence is the sort of detail that makes a message read as a machine error.</para>
    /// </summary>
    private static string Size(long bytes)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        const long gigabyte = 1024L * 1024 * 1024;

        return bytes >= gigabyte
            ? (bytes / (double)gigabyte).ToString("0.0", fr) + " Go"
            : (bytes / (1024d * 1024)).ToString("0.0", fr) + " Mo";
    }
}

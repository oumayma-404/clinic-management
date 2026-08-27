namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Reads a cabinet's rows out to JSON and puts missing ones back — the one part of the archive that has to know
/// about EF Core, and therefore the only part that lives in Infrastructure.
///
/// <para><b>Why the seam is at JSON and not at « a row ».</b> A row's values are CLR values — a
/// <c>Guid</c>, a <c>DateTime</c>, an enum, a <c>decimal</c> — and only the model knows which. Handing Application
/// an <c>object?</c> bag would mean serializing it there, where the types are gone, so a <c>DateTime</c> would
/// come back a <c>string</c> and a <c>decimal</c> a <c>double</c>. Serializing on this side keeps the round trip
/// exact; Application owns the zip, the manifest and every decision about what a restore <i>means</i>.</para>
///
/// <para>⚠️ <b>Nothing here goes through a domain constructor</b>, and it cannot. Every primary key in this product
/// is a GUID minted in the constructor and half the timestamps are stamped there from <c>DateTime.UtcNow</c>, so
/// building entities the ordinary way would give every restored row a new identity and today's date — which is the
/// opposite of a restore. Rows are materialised straight onto the model's own properties instead.</para>
/// </summary>
public interface IClinicArchiveStore
{
    /// <summary>
    /// Every archived table of <paramref name="clinicId"/>, <b>parents before children</b>, each as a JSON array
    /// with the storage keys its rows point at.
    ///
    /// <para><b>One call rather than one per table, and the ordering is why.</b> A table with a
    /// <c>ClinicId</c> of its own is selected on it directly; a child that has none — an invoice line, a payment,
    /// a charted tooth — is selected by <i>its parent's</i> ids, which only exist once the parent has been read.
    /// The walk therefore carries state, and splitting it per table would push that state onto the caller, where
    /// getting the order wrong reads as a table that is simply empty.</para>
    ///
    /// <para>Scoped by an <b>explicit predicate</b>, not by relying on the ambient query filter: the filter is a
    /// backstop, and this is the one read in the product where a miss puts another cabinet's patients in a file
    /// the practice keeps on a laptop (AC-1).</para>
    /// </summary>
    Task<ClinicArchiveExport> ExportAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-inserts the rows of <paramref name="json"/> that are <b>missing</b> from <paramref name="clinicId"/>,
    /// with their original ids, and leaves everything else alone.
    ///
    /// <para>Three outcomes per row, and the third is the point: <b>present and identical</b> (counted, untouched),
    /// <b>present and different</b> (counted separately and <i>skipped</i> — work done since the archive was taken
    /// must not be rolled back, AC-4), and <b>absent</b> (re-inserted, AC-3). Nothing is ever updated or
    /// deleted, which is what makes a second restore a no-op (AC-2).</para>
    ///
    /// <para>Rows are staged, not committed: the caller saves, so the restore's audit rows ride the same
    /// transaction as the rows they describe.</para>
    ///
    /// <para>⚠️ <b>Call it in the plan's own order</b> — the manifest's — and once per table. A row is admitted
    /// only when the cabinet legitimately owns whatever it hangs off, which is knowledge this call accumulates as
    /// it walks: a payment is checked against the invoices established by the table before it. Out of order, the
    /// children of a table not yet seen are all refused.</para>
    /// </summary>
    Task<ClinicArchiveTableOutcome> RestoreTableAsync(
        string table,
        Guid clinicId,
        string json,
        CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="table"/> is one this build knows how to restore — an unknown one is skipped and named.</summary>
    bool CanRestore(string table);

    /// <summary>
    /// Drops the rows already committed from change tracking, between one table's save and the next.
    ///
    /// <para>Exists for <see cref="IUnitOfWork.StopTracking"/>'s reason, one level up: a restore saves once per
    /// table and EF re-scans every tracked entry on each save, so a full-cabinet restore across thirty tables
    /// would be quadratic in precisely the case this feature is written for. Call it only <b>after</b> a
    /// successful save — dropping an <c>Added</c> entry before its commit discards the insert in silence.</para>
    ///
    /// <para>⚠️ It releases <b>the rows this restore staged</b> and nothing else. Clearing the whole change
    /// tracker would also drop whatever the request is holding — the caller's own <c>User</c>, and anything a
    /// handler staged before calling in — and the symptom of that is a discarded insert reported as a success.</para>
    /// </summary>
    void ForgetRestoredRows();
}

/// <summary>The whole of a cabinet's rows, in the order a restore must apply them.</summary>
/// <param name="Tables">One entry per archived table, parents first. A table with no rows is still listed.</param>
/// <param name="StorageKeys">Every blob key the rows point at, de-duplicated.</param>
/// <param name="Warnings">
/// What could not be archived, in French. Non-empty is not a failure: a table the model gained with no path to a
/// clinic is a table this walk cannot scope, and saying so beats omitting it silently.
/// </param>
public sealed record ClinicArchiveExport(
    IReadOnlyList<ClinicArchiveTableData> Tables,
    IReadOnlyList<string> StorageKeys,
    IReadOnlyList<string> Warnings);

/// <summary>One table's rows as they are written into the archive.</summary>
public sealed record ClinicArchiveTableData(string Table, string Json, int RowCount);

/// <summary>
/// What one table's restore did. Every row is accounted for in exactly one of the three counts.
///
/// <para><paramref name="StorageKeys"/> holds the blob keys of the rows this call <b>inserted</b> — not every key
/// the file names. That distinction is the whole of the blob half's tenancy: parsing the table for keys returned
/// them regardless of whether the owning row was written, skipped or refused, so an archive naming
/// <c>clinics/&lt;another practice&gt;/…</c> against a row that already existed had its bytes written into that
/// practice's prefix while the report read innocuously.</para>
///
/// <para><paramref name="Warnings"/> is what this table could not put back and why, in French — a row refused for
/// its parent, a document number already taken, an identifier held elsewhere. Counted <i>and</i> named: a
/// conflict total tells an owner something was skipped, and only the sentence tells them which thing.</para>
/// </summary>
public sealed record ClinicArchiveTableOutcome(
    int Restored,
    int AlreadyPresent,
    int Conflicts,
    IReadOnlyList<string>? StorageKeys = null,
    IReadOnlyList<string>? Warnings = null)
{
    public static ClinicArchiveTableOutcome Empty { get; } = new(0, 0, 0);

    public int Total => Restored + AlreadyPresent + Conflicts;

    public IReadOnlyList<string> BlobKeys => StorageKeys ?? Array.Empty<string>();

    public IReadOnlyList<string> Notices => Warnings ?? Array.Empty<string>();
}

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Removes every row this deployment holds for one cabinet — the database half of the console's
/// « supprimer définitivement ce cabinet ».
///
/// <para><b>Why this is an outbound interface and not a repository.</b> There is no aggregate here: the work is a
/// pass over every table that can hold a clinic's rows, in an order the foreign keys dictate, and the only
/// authority on both the set and the order is the EF model. That lives in Infrastructure, so the handler states
/// the intention and the implementation derives the plan — which is what keeps a table added next month covered
/// without anybody remembering this feature exists.</para>
///
/// <para><b>⚠️ Both members take the cabinet's addresses as well as its id</b>, because two tables belong to a
/// cabinet and carry no <c>ClinicId</c> to find them by: a pending signup and a password-reset request are keyed
/// by e-mail — there is no clinic yet when a signup exists, and neither table has a foreign key to
/// <c>Users</c>. They hold the practice's name, its administrator's address and a hash of their password, so
/// leaving them is a cabinet's data outliving the cabinet. The addresses are read while the accounts still exist
/// and handed in; afterwards the answer is structurally unavailable.</para>
///
/// <para>⚠️ <b>It participates in the caller's transaction and opens none of its own.</b> The journal row that says
/// who deleted the cabinet is written by the same <c>SaveChangesAsync</c>, and « the cabinet is gone with no record
/// of who removed it » must not be a state this product can reach.</para>
/// </summary>
public interface IClinicPurge
{
    /// <summary>
    /// What a deletion would remove, without removing anything — the figures the console states before the vendor
    /// can commit. Counted through the very plan that does the deleting, so the preview cannot describe a
    /// different set of tables from the one that is emptied.
    /// </summary>
    Task<ClinicPurgeCensus> CountAsync(
        Guid clinicId, IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the cabinet's rows and returns what was removed, counted inside the same transaction immediately
    /// before each delete.
    ///
    /// <para>⚠️ It <b>verifies itself</b>: after the pass, every table it covers is counted again and a single
    /// surviving row throws, which rolls the caller's transaction back. A partially emptied cabinet is the one
    /// outcome worse than a refusal — it reads as deleted, cannot be signed into, and still holds patient rows.</para>
    /// </summary>
    Task<ClinicPurgeCensus> PurgeAsync(
        Guid clinicId, IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default);
}

/// <summary>One table's contribution to a cabinet's footprint. <paramref name="Entity"/> is the CLR name, which is
/// what a caller names a figure by; <paramref name="Table"/> is what the SQL touched, for the log.</summary>
public sealed record ClinicPurgeTally(string Entity, string Table, long Rows);

/// <summary>
/// Everything one cabinet holds. Only tables with rows are listed — a census of sixty zeroes tells a reader
/// nothing and would make the total harder to find than the noise around it.
/// </summary>
public sealed record ClinicPurgeCensus(IReadOnlyList<ClinicPurgeTally> Tallies, long FileBytes)
{
    public static ClinicPurgeCensus Empty { get; } = new(Array.Empty<ClinicPurgeTally>(), 0);

    /// <summary>Every row the cabinet owns, across every table.</summary>
    public long Rows => Tallies.Sum(t => t.Rows);

    /// <summary>How many rows one entity contributes, or 0 — never null, so a caller states « 0 patient » rather
    /// than nothing at all.</summary>
    public long RowsOf(string entity) =>
        Tallies.FirstOrDefault(t => string.Equals(t.Entity, entity, StringComparison.Ordinal))?.Rows ?? 0;
}

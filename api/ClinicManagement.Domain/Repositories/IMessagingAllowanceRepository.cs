using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The WhatsApp reminder allocation ledger and its per-month counting rows. Mutations only stage; the Application
/// <c>IUnitOfWork</c> commits — which is what lets provisioning put a cabinet, its entitlement and its allowance in
/// <b>one</b> save (FR-3), and what lets a send and its counted unit ride one transaction (FR-1, EC-14).
///
/// <para>Both tables carry a non-nullable <c>ClinicId</c> and are filtered, so there is <b>no
/// <c>IgnoreQueryFilters()</c> anywhere</b> in the implementation and none is needed: a cross-cabinet caller declares
/// <c>UseSystemWide</c> rather than having the repository quietly read across practices.</para>
/// </summary>
public interface IMessagingAllowanceRepository
{
    /// <summary>
    /// The cabinet's <b>whole</b> allocation ledger, oldest first.
    ///
    /// <para>⚠️ Deliberately <b>not paged</b>, on <c>IClinicSubscriptionRepository.GetEntriesAsync</c>'s stated
    /// reason: every caller either folds it — and a fold over a page is not a fold — or is the console's own history,
    /// which folds the whole ledger for its per-entry consequence and then cuts a page in memory.</para>
    /// </summary>
    Task<IReadOnlyList<MessagingAllowanceEntry>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>One entry of one cabinet, or null. The cancellation path's lookup — scoped, so another practice's is unreachable.</summary>
    Task<MessagingAllowanceEntry?> GetEntryAsync(
        Guid clinicId, Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The counting row for one (cabinet, month), or <b>null</b> where none has ever been written.
    ///
    /// <para>⚠️ Null is a first-class answer and must not be defaulted into a zeroed row by a caller: « non mesuré »
    /// and « 0 rappel envoyé » are opposite claims (AC-2.4, AC-8.3).</para>
    /// </summary>
    Task<ClinicMessagingMonth?> GetMonthAsync(
        Guid clinicId, string monthKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cabinet's counting rows from <paramref name="fromMonthKey"/> onwards, oldest first — the history read
    /// (AC-2.3) and the refold's working set. Months with no row are simply absent, which is what lets the caller
    /// tell a gap (« non mesuré ») from a quiet month.
    /// </summary>
    Task<IReadOnlyList<ClinicMessagingMonth>> GetMonthsAsync(
        Guid clinicId, string fromMonthKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every cabinet of the deployment beside its row for <paramref name="monthKey"/>, or beside <c>null</c> where it
    /// has none — the <c>messaging-report</c> verb's one read (AC-8.6, AC-9.4).
    ///
    /// <para>⚠️ <b>A cabinet with no row is a row here, not an omission.</b> Keying the report off the counting table
    /// would make « the pass has not run » the one state the report cannot show, which is the opposite of what a
    /// safety net is for (FR-1a).</para>
    ///
    /// <para>⚠️ Meaningful only under <c>UseSystemWide</c>: <c>Clinics</c> carries no query filter while these two
    /// tables do, so under a <c>UseClinic(x)</c> scope every <i>other</i> cabinet would come back looking unmeasured.</para>
    /// </summary>
    Task<IReadOnlyList<ClinicMessagingReportRow>> GetForReportAsync(
        string monthKey, CancellationToken cancellationToken = default);

    Task AddEntryAsync(MessagingAllowanceEntry entry, CancellationToken cancellationToken = default);

    Task AddMonthAsync(ClinicMessagingMonth month, CancellationToken cancellationToken = default);

    Task UpdateEntryAsync(MessagingAllowanceEntry entry, CancellationToken cancellationToken = default);

    Task UpdateMonthAsync(ClinicMessagingMonth month, CancellationToken cancellationToken = default);
}

/// <summary>
/// One cabinet as the messaging report sees it. It carries the <b>entities</b> rather than flattened columns so the
/// report can apply the real fold and the real per-month figures, instead of re-deriving « is this cabinet exhausted? »
/// from a projection — the <c>ClinicSubscriptionReportRow</c> precedent.
/// </summary>
/// <param name="Month">Null where the cabinet has no counting row for the month asked about (FR-1a's failure state).</param>
public sealed record ClinicMessagingReportRow(
    Guid ClinicId,
    string ClinicName,
    ClinicMessagingMonth? Month);

using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The entitlement and its ledger. Mutations only stage; the Application <c>IUnitOfWork</c> commits — which is what
/// lets provisioning put a cabinet and its entitlement in <b>one</b> save (FR-4's « one indivisible operation »).
/// </summary>
public interface IClinicSubscriptionRepository
{
    /// <summary>The cabinet's entitlement, or null — which the gate reports as <c>subscription_missing</c> (EC-6).</summary>
    Task<ClinicSubscription?> GetByClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cabinet's <b>whole</b> ledger, oldest first.
    ///
    /// <para>⚠️ Deliberately not paged. Every caller either folds it — and a fold over a page is not a fold
    /// (<c>ClinicSubscription.RecomputeFrom</c>) — or is the history screen, which folds the whole ledger for its
    /// derived « période couverte » and then cuts a page in memory with <c>PagedResult.FromSource</c>.</para>
    /// </summary>
    Task<IReadOnlyList<SubscriptionPeriod>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every cabinet of the deployment beside its entitlement, or beside <c>null</c> where it has none — the
    /// vendor report's one read (AC-5.9).
    ///
    /// <para>⚠️ <b>A cabinet with no entitlement is a row here, not an omission.</b> Keying the report off the
    /// entitlement table would make FR-13's failure — a cabinet that somehow has none — the one state the report
    /// cannot show, which is the opposite of what a safety net is for.</para>
    ///
    /// <para>⚠️ <b>Only meaningful under <c>UseSystemWide</c>.</b> <c>Clinics</c> carries no query filter while
    /// <c>ClinicSubscriptions</c> does, so under a <c>UseClinic(x)</c> scope every <i>other</i> cabinet would come
    /// back looking as though its entitlement were missing.</para>
    /// </summary>
    Task<IReadOnlyList<ClinicSubscriptionReportRow>> GetForReportAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the <b>vendor</b> was paid across every cabinet over an inclusive window — the summary strip's
    /// « encaissé par l'éditeur » (<c>platform-console</c> AC-2.7).
    ///
    /// <para>⚠️ <b>Never a sum of the cabinets' own turnover</b>, which is a different figure with a different name
    /// measured over different rows (FR-2). Cancelled entries are excluded, and an entry with no amount — a
    /// complimentary period, AC-4.8 — contributes nothing rather than being skipped as a row.</para>
    ///
    /// <para>⚠️ Meaningful only under <c>UseSystemWide</c>, like <see cref="GetForReportAsync"/>.</para>
    /// </summary>
    Task<decimal> GetVendorCollectedBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task AddAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default);

    Task AddEntryAsync(SubscriptionPeriod entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default);
}

/// <summary>
/// One cabinet as the vendor report sees it. It carries the <b>entity</b> rather than flattened columns so the
/// report can apply the real <c>SubscriptionStateReader</c> — the one FR-1 rule the gate, the screen, the banner and
/// the warning job also read — instead of re-deriving « is this cabinet expired? » from a projection.
/// </summary>
/// <param name="Subscription">Null where the cabinet has no entitlement at all (FR-13's failure state).</param>
public sealed record ClinicSubscriptionReportRow(
    Guid ClinicId,
    string ClinicName,
    ClinicSubscription? Subscription);

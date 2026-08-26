using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

public interface IAuditEntryRepository
{
    /// <summary>
    /// The clinic's audit ledger, newest first, filtered and paged in SQL.
    ///
    /// <para>Ordered <c>OccurredAt</c> descending then <c>Id</c> — a busy save writes several rows in the same
    /// tick, and <c>OFFSET</c> over a non-unique sort can show one row on two pages and skip another, which on
    /// this table reads as « the record of that deletion disappeared ».</para>
    ///
    /// <para><paramref name="from"/>/<paramref name="to"/> are inclusive UTC bounds; the caller derives them from
    /// <c>ClinicClock</c> so « le 3 août » means the clinic's day, not UTC's.</para>
    /// </summary>
    /// <param name="userId">
    /// One actor's entries — « qu'a fait cette personne ? ». Matched exactly on the stored id, which is a
    /// <c>User.Id</c> for a person and a prefixed value for a process or the vendor's console (see
    /// <c>AuditActor</c>), so the same parameter also isolates « ce qu'a fait la tâche automatique ».
    /// </param>
    Task<PagedResult<AuditEntry>> GetFilteredAsync(
        Guid clinicId,
        string? entityType = null,
        string? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        AuditAction? action = null,
        string? userId = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The entity types this clinic actually has rows for, ordered by name — what the screen's « Type » filter can
    /// offer. Derived from the data rather than from a hand-kept list of auditable types, for the reason the
    /// interceptor exists at all: a list somebody has to remember to extend is a list that stops being true.
    /// </summary>
    Task<IReadOnlyList<string>> GetRecordedEntityTypesAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The actors this clinic actually has rows for — id plus the email recorded with it — so the screen's
    /// « Auteur » filter can offer real people rather than asking an admin to paste a user id.
    ///
    /// <para>Derived from the ledger and not from the <c>Users</c> table, deliberately: a colleague who has since
    /// left still appears in the history, and « qu'a fait cette personne ? » is asked about them most of all. It
    /// also covers the background jobs and the vendor's console, which are not clinic users at all.</para>
    ///
    /// <para>An id can carry more than one email over time (an address change); the newest is kept, because it is
    /// the one an admin will recognise.</para>
    /// </summary>
    Task<IReadOnlyList<AuditActorRow>> GetRecordedActorsAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends rows. Called only by the audit interceptor's own short-lived context — see
    /// <c>AuditSaveChangesInterceptor</c> for why the ledger is written outside the business save's transaction.
    /// </summary>
    Task AddRangeAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// The four columns the vendor console's counter pass measures a cabinet's activity from, over an inclusive
    /// UTC window (<c>platform-console</c> AC-2.1/AC-2.2).
    ///
    /// <para><b>A projection and not <see cref="GetFilteredAsync"/> with no paging.</b> The pass runs for every
    /// cabinet on every run and needs four scalars per row; materialising whole <see cref="AuditEntry"/>
    /// instances would drag <c>ChangedFields</c> — unbounded text, up to 512 characters a row — through the job
    /// for nothing.</para>
    ///
    /// <para>⚠️ <b>The actor exclusions are deliberately NOT applied here.</b> They are
    /// <c>PlatformCounterPass</c>'s, matched on <c>AuditActor</c>'s own prefix constants, because that is the
    /// only place they can be unit-tested — and a second copy of « what counts as a person at the cabinet » is
    /// a filter that keeps passing while the writer moves.</para>
    /// </summary>
    Task<IReadOnlyList<ClinicActivityAuditRow>> GetActivityRowsAsync(
        Guid clinicId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>One distinct actor in a clinic's ledger: the stored id and the most recent email seen with it.</summary>
public record AuditActorRow(string UserId, string? UserEmail);

/// <summary>One audit row, reduced to what an activity count depends on.</summary>
public record ClinicActivityAuditRow(string UserId, string EntityType, AuditAction Action, DateTime OccurredAt);

using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One appointment a run created, with every fact « may this be undone? » turns on — assembled by the repository
/// in a handful of batched queries rather than by the handler in one per row.
///
/// <para>It is a <b>Domain</b> record for <c>PatientLinkedDataCounts</c>' reason: the projection is what the
/// repository can answer, and the Application rule that reads it (<c>CalendarImportRevertRules</c>) must not be
/// something Infrastructure has to reference in order to be asked a question.</para>
/// </summary>
public sealed record CalendarImportRunVisit(
    Guid AppointmentId,
    Guid? PatientId,
    string PatientName,
    DateTime AppointmentDateTime,
    bool HasFiche,
    bool HasLiveInvoice,
    bool CoveredByPlan,
    bool HasLabOrder,
    bool HasProcedures,
    bool NothingToBill,
    bool Disregarded);

/// <summary>One placeholder patient a run created, named for the screen that lists what will go.</summary>
public sealed record CalendarImportRunPatient(Guid PatientId, string FullName);

/// <summary>Everything a run still owns. Empty on both sides once it has been undone.</summary>
public sealed record CalendarImportRunContents(
    IReadOnlyList<CalendarImportRunVisit> Visits,
    IReadOnlyList<CalendarImportRunPatient> Patients);

public interface ICalendarImportRunRepository
{
    Task<CalendarImportRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the clinic's runs, newest first. Always paged, like the backup ledger: the recurring import
    /// writes a row per clinic per pass, so the table grows for as long as the practice stays connected and
    /// nothing legitimately wants all of it.
    /// </summary>
    Task<PagedResult<CalendarImportRun>> GetHistoryAsync(
        Guid clinicId, PageRequest? paging, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's most recent run that <b>still has rows</b> — the one « Annuler cet import » offers, and the
    /// one the banner on « À clôturer » names.
    ///
    /// <para>Not simply « the latest »: the recurring pass writes a run every few hours, and most of them create
    /// nothing. Offering to undo an empty run would put a destructive-looking button in front of a practice for
    /// no reason, and would hide the one import that actually filled its worklist behind a dozen that did not.</para>
    /// </summary>
    Task<CalendarImportRun?> GetLatestUndoableAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything <paramref name="runId"/> created and still owns, with the facts the undo rules read.
    ///
    /// <para><b>Clinic-scoped as well as run-scoped</b>, and that is not belt-and-braces: the run id arrives from
    /// a URL, and a run belonging to another practice must read as empty rather than as a set of rows somebody is
    /// about to be offered the chance to delete.</para>
    /// </summary>
    Task<CalendarImportRunContents> GetContentsAsync(
        Guid clinicId, Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage the deletion of the named rows and everything that would be left pointing at them.
    ///
    /// <para><b>The delete ORDER lives here, in one place, because it is a property of the undo rather than of any
    /// one aggregate.</b> ⚠️ The load-bearing part is what goes first: <c>Notification.AppointmentId</c> and
    /// <c>PushDelivery</c>'s are <c>OnDelete(SetNull)</c>, so deleting an appointment does <b>not</b> take its
    /// queued reminder with it — it orphans it with a null link, and the minutely dispatcher still sends it. A
    /// patient would receive « Rappel : votre rendez-vous demain » for a visit that no longer exists, hours after
    /// the practice undid the import. Spreading this across five repositories is how one of them ends up not
    /// knowing about the others.</para>
    ///
    /// <para>Stages only — the caller commits, so the whole undo is one transaction and a half-reverted run
    /// cannot exist.</para>
    /// </summary>
    Task DeleteRunRowsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> appointmentIds,
        IReadOnlyCollection<Guid> patientIds,
        CancellationToken cancellationToken = default);

    Task AddAsync(CalendarImportRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(CalendarImportRun run, CancellationToken cancellationToken = default);
}

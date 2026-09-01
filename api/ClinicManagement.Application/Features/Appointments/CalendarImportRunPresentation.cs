using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// How a run and its rows read on screen — shared by the list, the preview and the undo's own result.
///
/// <para>Shared rather than written three times because all three answer the same question (« what did this pass
/// create, and what would go? ») and a preview that promised something the undo then did differently would be the
/// one defect this feature cannot afford: the preview <b>is</b> the safety, since the person pressing the button
/// is the cabinet rather than the vendor.</para>
/// </summary>
public static class CalendarImportRunPresentation
{
    /// <summary>
    /// What the screen calls a pass nobody clicked. The stored actor is <c>job|GoogleCalendarImportJob</c> — an
    /// audit-ledger convention, not a sentence — and printing it raw would be the first time a dentist saw the
    /// inside of the audit format.
    /// </summary>
    public const string AutomaticLabel = "Import automatique";

    /// <summary>The fallback when the actor is a user id nothing could resolve to a name.</summary>
    public const string UnknownActorLabel = "Import manuel";

    /// <summary>
    /// The actor the <c>AddCalendarImportRunsAndWorklistDismissal</c> backfill stamps on a run it reconstructed
    /// from history.
    /// </summary>
    public const string BackfillActor = CalendarImportRun.JobActorPrefix + "CalendarImportBackfill";

    /// <summary>
    /// What a reconstructed run calls itself. ⚠️ Deliberately <b>not</b> « Import automatique »: the backfill
    /// genuinely does not know who pressed the button — the rows it recovered predate any run record — and
    /// claiming the schedule did it would be a small lie on the one screen a practice reads to decide whether to
    /// delete a hundred rows.
    /// </summary>
    public const string BackfilledLabel = "Import précédent";

    public static string DescribeActor(string triggeredByUserId, string? resolvedName) =>
        string.Equals(triggeredByUserId, BackfillActor, StringComparison.Ordinal)
            ? BackfilledLabel
            : triggeredByUserId.StartsWith(CalendarImportRun.JobActorPrefix, StringComparison.Ordinal)
                ? AutomaticLabel
                : string.IsNullOrWhiteSpace(resolvedName) ? UnknownActorLabel : resolvedName;

    public static CalendarImportRunDto ToDto(CalendarImportRun run, string? actorName, int rowsRemaining) =>
        new()
        {
            Id = run.Id,
            StartedAtUtc = run.StartedAtUtc,
            TriggeredBy = DescribeActor(run.TriggeredByUserId, actorName),
            AppointmentsCreated = run.AppointmentsCreated,
            PatientsCreated = run.PatientsCreated,
            AppointmentsUpdated = run.AppointmentsUpdated,
            RevertedAtUtc = run.RevertedAtUtc,
            RowsRemaining = rowsRemaining
        };

    /// <summary>
    /// Split a run's visits into the ones an undo may delete and the ones it must keep, each keeper carrying its
    /// reason as a printable French sentence.
    /// </summary>
    public static (List<Guid> Deletable, List<CalendarImportKeptRowDto> Kept) PartitionVisits(
        IReadOnlyList<CalendarImportRunVisit> visits)
    {
        var deletable = new List<Guid>();
        var kept = new List<CalendarImportKeptRowDto>();

        foreach (var visit in visits)
        {
            var input = new ImportedVisit(
                AppointmentId: visit.AppointmentId,
                HasFiche: visit.HasFiche,
                HasLiveInvoice: visit.HasLiveInvoice,
                CoveredByPlan: visit.CoveredByPlan,
                HasLabOrder: visit.HasLabOrder,
                HasProcedures: visit.HasProcedures,
                NothingToBill: visit.NothingToBill,
                Disregarded: visit.Disregarded);

            var blockers = CalendarImportRevertRules.BlockersFor(input);

            if (blockers.Count == 0)
            {
                deletable.Add(visit.AppointmentId);
                continue;
            }

            kept.Add(new CalendarImportKeptRowDto
            {
                Id = visit.AppointmentId,
                Label = visit.PatientName,
                When = visit.AppointmentDateTime,
                Reason = CalendarImportRevertRules.Describe(blockers)
            });
        }

        return (deletable, kept);
    }
}

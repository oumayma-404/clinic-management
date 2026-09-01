using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// « Annuler cet import » — undo one Google→App pass, deleting exactly the rows it created and nothing has
/// touched since.
///
/// <para><b>Why the product needed this.</b> The import was a one-click, unbounded, irreversible bulk write: it
/// pulled a 97-day window in one call, turned every past event into a visit that landed on « À clôturer », and
/// conjured a placeholder patient per unmatched title. A cabinet that pressed it once had no way back — and the
/// two ways it tried made things worse, because cancelling a visit both counts as a missed one in the taux
/// d'absence and <b>deletes the matching event from the practice's own Google calendar</b>.</para>
///
/// <para><b>The statistics need no repair code.</b> Every activity figure is a derived read over appointment
/// rows, so deleting the phantom rows — the ones already cancelled included, since they carry the same stamp —
/// makes the arithmetic correct again with nothing to remember it was ever wrong.</para>
///
/// <para>⚠️ <b>It must never speak to Google.</b> No <c>IAppointmentGoogleSyncDispatcher</c> is injected and no
/// deletion goes through <c>Appointment.Cancel()</c>: that status is what makes
/// <c>GoogleCalendarSyncService</c> delete the event, so an undo routed that way would finish destroying the
/// calendar it exists to protect. The rows are removed outright.</para>
///
/// <para>⚠️ <b><c>AdminOnly</c></b>, unlike the rest of the worklist — it deletes patient records.</para>
///
/// <para><b>It lives under <c>Features/Appointments</c> for the realtime key.</b> A <c>Features/CalendarImports</c>
/// folder would emit a key <c>web/lib/realtime/clinic-hub.ts</c> does not declare, and
/// <c>RealtimeResourceResolverTests</c> compares the two sets in both directions. The <c>patients</c> key it also
/// needs is broadcast explicitly below.</para>
/// </summary>
public class RevertCalendarImportRunCommand : IRequest<Result<CalendarImportRevertResultDto>>
{
    public Guid RunId { get; set; }
}

public class RevertCalendarImportRunCommandHandler
    : IRequestHandler<RevertCalendarImportRunCommand, Result<CalendarImportRevertResultDto>>
{
    /// <summary>Branch on this, never on the sentence — a reworded message must not change client behaviour.</summary>
    public const string AlreadyRevertedCode = "calendar_import_already_reverted";

    /// <summary>
    /// The undo could not take a recovery point first, so it did nothing.
    ///
    /// <para>⚠️ <b>A refusal and not a warning.</b> This is a self-serve bulk delete of patient records with no
    /// vendor in the loop: nobody is holding a backup and nobody is watching the row counts. « Supprimer sans
    /// filet » is not a choice to put in front of a practice mid-consultation, so no net means no delete.</para>
    /// </summary>
    public const string NoRecoveryPointCode = "calendar_import_no_recovery_point";

    /// <summary>
    /// The <c>patients</c> key, asked of the production resolver rather than typed as a literal.
    /// <c>AppointmentProgressJob</c>'s precedent and its reason: a typed key is a second authority over a contract
    /// held against the frontend, and a wrong one is a broadcast nobody listens for — which looks exactly like the
    /// feature not running.
    /// </summary>
    private static readonly string PatientsResource =
        RealtimeResourceResolver.Resolve(typeof(DismissCalendarReviewCommand))!;

    private readonly ICalendarImportRunRepository _runRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IClinicRecoveryPointService _recoveryPoints;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<RevertCalendarImportRunCommandHandler> _logger;

    public RevertCalendarImportRunCommandHandler(
        ICalendarImportRunRepository runRepository,
        IPatientRepository patientRepository,
        IClinicRecoveryPointService recoveryPoints,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IRealtimeNotifier realtime,
        ILogger<RevertCalendarImportRunCommandHandler> logger)
    {
        _runRepository = runRepository;
        _patientRepository = patientRepository;
        _recoveryPoints = recoveryPoints;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<CalendarImportRevertResultDto>> Handle(
        RevertCalendarImportRunCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CalendarImportRevertResultDto>.Failure(clinicResult.Error ?? ErrorMessages.Generic);
            }

            var clinicId = clinicResult.Value;
            var run = await _runRepository.GetByIdAsync(request.RunId, cancellationToken);

            // One refusal for « does not exist » and « belongs to another clinic ».
            if (run is null || run.ClinicId != clinicId)
            {
                return Result<CalendarImportRevertResultDto>.Failure("Import introuvable.");
            }

            if (run.IsReverted)
            {
                return Result<CalendarImportRevertResultDto>.Failure(
                    "Cet import a déjà été annulé.", AlreadyRevertedCode);
            }

            var contents = await _runRepository.GetContentsAsync(clinicId, run.Id, cancellationToken);

            // ⚠️ EVERY refusal is decided here, before a single row is staged for deletion — the rule
            // `MergeIntoSuggestedDuplicateCommand` states for itself: a refusal that has already deleted half the
            // rows is not a refusal.
            var (deletableAppointmentIds, kept) = CalendarImportRunPresentation.PartitionVisits(contents.Visits);

            var deletablePatientIds = await ResolveDeletablePatientsAsync(
                contents, deletableAppointmentIds, kept, cancellationToken);

            // ⚠️ THE NET, taken before the first row is staged for deletion, and a hard gate.
            //
            // Everything above makes this delete conservative — six blockers, a preview the user confirmed, a
            // stamp only the import writes. This is what stands behind a mistake in *that* reasoning. It matters
            // here and not on an ordinary delete because the person pressing the button is the cabinet rather
            // than the vendor: nobody is holding a backup, nobody is watching the row counts, and the action
            // removes patient records in bulk.
            //
            // Skipped when there is nothing to delete — a run whose rows have all been kept costs a practice no
            // archive, and a recovery point for a no-op would be seven days of retention spent on nothing.
            if (deletableAppointmentIds.Count > 0 || deletablePatientIds.Count > 0)
            {
                if (!await _recoveryPoints.TryTakeAsync(clinicId, cancellationToken))
                {
                    // Named, so the practice knows what to fix rather than meeting « une erreur est survenue » on
                    // the one action it came here for. Nothing has been deleted.
                    return Result<CalendarImportRevertResultDto>.Failure(
                        "Aucune sauvegarde n'a pu être créée avant l'annulation, et rien n'a été supprimé. "
                        + "Réessayez dans un instant ; si le problème persiste, contactez le support.",
                        NoRecoveryPointCode);
                }
            }

            await _runRepository.DeleteRunRowsAsync(
                clinicId, deletableAppointmentIds, deletablePatientIds, cancellationToken);

            run.MarkReverted(
                DateTime.UtcNow,
                _clinicContext.GetUserId() ?? string.Empty,
                deletableAppointmentIds.Count,
                deletablePatientIds.Count,
                kept.Count);

            await _runRepository.UpdateAsync(run, cancellationToken);

            // One save for the whole undo. A partially-reverted run cannot exist — which matters more here than
            // anywhere else in this feature, because the run's own « annulé » stamp and the rows it describes
            // would otherwise be able to disagree.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reverted calendar import run {RunId} for clinic {ClinicId}: {Appointments} appointment(s) and "
                + "{Patients} patient(s) deleted, {Kept} row(s) kept",
                run.Id, clinicId, deletableAppointmentIds.Count, deletablePatientIds.Count, kept.Count);

            // The `appointments` key comes from this namespace through RealtimeBroadcastBehavior; `patients` does
            // not, and the undo deletes those too. Post-commit and best-effort, like every other broadcast.
            try
            {
                await _realtime.NotifyEntityChangedAsync(clinicId, PatientsResource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast the patients refresh after reverting import {RunId}", run.Id);
            }

            return Result<CalendarImportRevertResultDto>.Success(new CalendarImportRevertResultDto
            {
                RunId = run.Id,
                AppointmentsDeleted = deletableAppointmentIds.Count,
                PatientsDeleted = deletablePatientIds.Count,
                Kept = kept
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to revert calendar import run {RunId}", request.RunId);
            return Result<CalendarImportRevertResultDto>.Failure(ErrorMessages.Generic);
        }
    }

    /// <summary>
    /// Which placeholder patients may go, appending a named row to <paramref name="kept"/> for each that may not.
    ///
    /// <para>⚠️ It cannot reuse <c>DeletePatientCommand</c>'s refusal (« anything at all is attached »): an
    /// imported placeholder always has at least the appointment it was created for, which is precisely what this
    /// undo is deleting. So the test is that refusal <b>minus this run's own rows</b> — the shape
    /// <c>MergeIntoSuggestedDuplicateCommand</c> already uses.</para>
    ///
    /// <para>The appointment term is a <i>count</i> comparison rather than a boolean: a placeholder the practice
    /// has since booked a real visit for holds more appointments than this run created, and deleting them would
    /// take that booking with it.</para>
    /// </summary>
    private async Task<List<Guid>> ResolveDeletablePatientsAsync(
        CalendarImportRunContents contents,
        IReadOnlyCollection<Guid> deletableAppointmentIds,
        List<CalendarImportKeptRowDto> kept,
        CancellationToken cancellationToken)
    {
        var deletable = new List<Guid>();

        var goingByPatient = contents.Visits
            .Where(v => v.PatientId.HasValue && deletableAppointmentIds.Contains(v.AppointmentId))
            .GroupBy(v => v.PatientId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var patient in contents.Patients)
        {
            var counts = await _patientRepository.GetLinkedDataCountsAsync(patient.PatientId, cancellationToken);

            goingByPatient.TryGetValue(patient.PatientId, out var going);

            // Appointments and notifications are this run's own doing and are being removed with it; everything
            // else is the practice's.
            var otherwiseAttached = counts.Total - counts.Appointments - counts.Notifications;
            var appointmentsStaying = counts.Appointments - going;

            if (otherwiseAttached <= 0 && appointmentsStaying <= 0)
            {
                deletable.Add(patient.PatientId);
                continue;
            }

            // Reuse the deletion vocabulary rather than inventing a second one — « 2 rendez-vous et 1 fiche de
            // soins » is the enumeration `PatientDeletionBlockers` already builds for the delete refusal. The two
            // terms this undo is itself removing are zeroed out of the counts first, or the sentence would name
            // rows that are about to disappear.
            var remaining = counts with
            {
                Appointments = Math.Max(0, appointmentsStaying),
                Notifications = 0
            };

            kept.Add(new CalendarImportKeptRowDto
            {
                Id = patient.PatientId,
                Label = patient.FullName,
                When = null,
                Reason = PatientDeletionBlockers.DescribeAttached(remaining)
            });
        }

        return deletable;
    }
}

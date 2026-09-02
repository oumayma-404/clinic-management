using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// « Retirer de la liste » — take one or many séances off « À clôturer » without claiming anything clinical about
/// them, or put them back.
///
/// <para><b>Why the worklist needed a third kind of exit.</b> <c>VisitClosureRules.IsClosable</c> lets a visit
/// leave only as <c>Completed</c>, <c>Cancelled</c> or <c>NoShow</c> — three statements about what happened to a
/// patient. A row that should never have been on the list answers none of them, so clearing it meant asserting one
/// that was false. A cabinet whose Google calendar import filled the list with a week of past events cancelled
/// them to tidy up, and two things happened that nobody wanted: <c>DashboardActivityReader</c> counts
/// <c>Cancelled</c> among the missed, so « taux d'absence » climbed to a figure the practice knew was wrong; and
/// <c>GoogleCalendarSyncService</c> deletes the Google event behind a cancelled visit, so tidying the app was
/// quietly deleting the practice's own calendar. This is the exit that asserts nothing.</para>
///
/// <para><b>« Rien à facturer » is not this, and neither replaces the other.</b> That mark answers the *third*
/// question — this séance raises no document — and leaves the first two standing. This one says the row does not
/// belong on the list at all. A visit can legitimately carry either.</para>
///
/// <para><b>It has one floor: a séance that carries work cannot be retired</b> — see
/// <c>FindVisitsCarryingWorkAsync</c>. This is also the write path behind « Supprimer (créé par erreur) » on the
/// appointment itself, which is the second call site the mark has ever had; the mechanism, the audit trail and the
/// exclusion from the figures are all the worklist's, unchanged.</para>
///
/// <para><b>One command with a <c>bool</c> and a list, three routes.</b> <see cref="MarkNothingToBillCommand"/>'s
/// shape: two handlers would be two copies of « resolve · tenant-check · mutate · save », and the direction lives
/// in the <b>URL</b> so no truncated body can turn « remettre » into « retirer ». The single-id routes send a
/// one-element list, so the bulk path is the only path and cannot drift from a single-row one.</para>
///
/// <para>⚠️ <b>No motif, and that is a deliberate reversal of how this first shipped.</b> It asked for one, on
/// <see cref="MarkNothingToBillCommand"/>'s reasoning — and the parallel was wrong. « Rien à facturer » is a claim
/// about money the cabinet may be asked to justify; this asserts nothing whatsoever, so there is nothing to
/// justify. Demanding a sentence to say « cette ligne ne me concerne pas », across the hundred-odd rows this
/// exists for, priced the honest exit above the dishonest one — and the dishonest one is <c>Cancel()</c>, which is
/// what inflated the « taux d'absence » in the first place. Who and when are still recorded, by
/// <c>AuditSaveChangesInterceptor</c>.</para>
///
/// <para><b><c>AnyClinicRole</c></b>, like the rest of the worklist: reception is who knows that the séance was a
/// duplicate or a slot nobody ever sat in. Nothing clinical is destroyed either way — the appointment, its status
/// and every record attached to it are untouched — and <c>AuditSaveChangesInterceptor</c> answers « qui a retiré
/// cette séance ? » afterwards.</para>
/// </summary>
public class DisregardVisitsCommand : IRequest<Result<DisregardVisitsResultDto>>
{
    public List<Guid> AppointmentIds { get; set; } = new();

    /// <summary>True to take the séances off the list, false to put them back.</summary>
    public bool Disregard { get; set; }
}

/// <summary>What the call did, in the terms the screen reports back.</summary>
public class DisregardVisitsResultDto
{
    public int Changed { get; set; }

    /// <summary>
    /// Ids the call did not touch — unknown, another clinic's, or already in the requested state. Reported rather
    /// than refused: a selection of a hundred rows overlapping a previous one is the ordinary case, and failing
    /// the whole batch over it would make the feature unusable exactly when it is needed.
    /// </summary>
    public List<Guid> Skipped { get; set; } = new();

    /// <summary>
    /// Ids the call <b>refused</b> because the séance carries real work — a fiche de soins, or a note d'honoraires
    /// that is not cancelled. Distinct from <see cref="Skipped"/>, which is « nothing to do »: this one is « non »,
    /// and the single-id route turns it into a 409 so the screen can say why.
    /// </summary>
    public List<Guid> Refused { get; set; } = new();
}

public class DisregardVisitsCommandHandler
    : IRequestHandler<DisregardVisitsCommand, Result<DisregardVisitsResultDto>>
{
    /// <summary>
    /// The most séances one call may retire. A bound at all because the body is a client-supplied list and this
    /// loads every row it names; generous enough for the case it exists for — a practice undoing a calendar import
    /// that filled the list with a week of phantom visits.
    /// </summary>
    public const int MaxIds = 500;

    /// <summary>
    /// Machine-readable tag on the refusal, so « Supprimer (créé par erreur) » can explain itself instead of
    /// showing a generic failure. A code and not the French sentence, for the reason the whole solution states:
    /// the message is reworded freely and matching on it would silently change behaviour.
    /// </summary>
    public const string VisitHasWorkCode = "visit_has_work";

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly ILogger<DisregardVisitsCommandHandler> _logger;

    public DisregardVisitsCommandHandler(
        IAppointmentRepository appointmentRepository,
        IDentalRecordRepository dentalRecordRepository,
        IInvoiceRepository invoiceRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        ILogger<DisregardVisitsCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _invoiceRepository = invoiceRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _logger = logger;
    }

    public async Task<Result<DisregardVisitsResultDto>> Handle(
        DisregardVisitsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var ids = request.AppointmentIds.Distinct().ToList();

            if (ids.Count == 0)
            {
                return Result<DisregardVisitsResultDto>.Failure("Aucune séance sélectionnée.");
            }

            if (ids.Count > MaxIds)
            {
                return Result<DisregardVisitsResultDto>.Failure(
                    $"Vous ne pouvez retirer que {MaxIds} séances à la fois.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DisregardVisitsResultDto>.Failure(clinicResult.Error ?? ErrorMessages.Generic);
            }

            var userId = _clinicContext.GetUserId() ?? string.Empty;
            var nowUtc = DateTime.UtcNow;
            var result = new DisregardVisitsResultDto();

            // Only when RETIRING. Putting a row back is always allowed — the guard exists to stop a séance leaving
            // the figures, never to strand one outside them.
            var carriesWork = request.Disregard
                ? await FindVisitsCarryingWorkAsync(clinicResult.Value, ids, cancellationToken)
                : new HashSet<Guid>();

            foreach (var id in ids)
            {
                var appointment = await _appointmentRepository.GetByIdAsync(id, cancellationToken);

                // One outcome for « does not exist » and « belongs to another clinic », deliberately: telling the
                // two apart would confirm that an id exists somewhere in the deployment.
                if (appointment is null || appointment.ClinicId != clinicResult.Value)
                {
                    result.Skipped.Add(id);
                    continue;
                }

                if (carriesWork.Contains(id))
                {
                    result.Refused.Add(id);
                    continue;
                }

                var before = appointment.IsDisregarded;

                if (request.Disregard)
                {
                    appointment.Disregard(userId, nowUtc);
                }
                else
                {
                    appointment.RestoreToWorklist();
                }

                if (appointment.IsDisregarded == before)
                {
                    // Already in the requested state. Both entity methods are idempotent, so this is not an error —
                    // it is simply not a change, and saying so is what lets the screen report « 138 retirées »
                    // rather than claiming it moved rows it did not.
                    result.Skipped.Add(id);
                    continue;
                }

                await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                result.Changed++;
            }

            // One save for the whole selection: the rows are independent, but a half-applied bulk action leaves
            // the user with no way to know where it stopped. Nothing here can fail per row — every refusal is
            // already decided above — so all-or-nothing costs nothing and removes that question.
            if (result.Changed > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<DisregardVisitsResultDto>.Success(result);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(
                ex, "Failed to set the disregarded mark on {Count} appointment(s)", request.AppointmentIds.Count);
            return Result<DisregardVisitsResultDto>.Failure(ErrorMessages.Generic);
        }
    }

    /// <summary>
    /// Which of these séances carry real work — a fiche de soins, or a note d'honoraires that is not cancelled.
    ///
    /// <para><b>Why the mark needs a floor at all.</b> « Retirer » means « ne comptez pas cette ligne », and the
    /// figures honour it. Over a séance that produced a fiche and a paid note, that is not a tidy-up: the visit
    /// leaves « Rendez-vous par statut » and the taux d'absence while its money stays in la caisse, so the two
    /// halves of the same day stop agreeing and nothing says why. « Créé par erreur » is simply false about a
    /// séance somebody treated a patient in — the exits for those are the three that assert something.</para>
    ///
    /// <para>Two batched reads for the whole selection, in <c>VisitClosureReader</c>'s shape, and the billing half
    /// goes through <see cref="AppointmentInvoiceLinks"/> rather than testing the status here: which note counts is
    /// a rule with one home, and a cancelled one bills nothing — a séance whose only note was voided is exactly the
    /// mis-booking this exists for.</para>
    /// </summary>
    private async Task<HashSet<Guid>> FindVisitsCarryingWorkAsync(
        Guid clinicId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var ficheLinks = await _dentalRecordRepository.GetAppointmentLinksAsync(clinicId, ids, cancellationToken);
        var invoiceLinks = await AppointmentInvoiceLinks.ResolveAsync(
            _invoiceRepository, clinicId, ids, cancellationToken);

        var carriesWork = ficheLinks.Select(l => l.AppointmentId).ToHashSet();
        carriesWork.UnionWith(invoiceLinks.Keys);
        return carriesWork;
    }
}

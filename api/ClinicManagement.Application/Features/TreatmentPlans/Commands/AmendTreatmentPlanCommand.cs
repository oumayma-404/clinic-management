using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Amend an accepted / in-progress devis: add acts, <b>edit the acts already on it</b>, remove acts, retitle it
/// and revise the échéancier — in one call, so the schedule can never be left out of sync with a total that
/// just changed.
/// <para>
/// Before this, a plan froze at acceptance and the only way to change treatment was Cancel + retype, losing
/// the devis number, the échéancier and every réalisé act.
/// </para>
/// <para>
/// <see cref="UpdateItems"/>, <see cref="Title"/> and <see cref="Notes"/> closed the remainder of that freeze.
/// The endpoint used to take additions and removals only, so "change this act's price" had to be expressed as
/// remove-then-add — which re-issues the act's id and is refused outright once the act is <c>Done</c> or
/// booked, i.e. exactly when a wrong price is usually noticed. Title and notes could not be touched at all.
/// </para>
/// </summary>
public class AmendTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// <para>
    /// It matters more here than it did when this command only appended acts: an amendment now rewrites fees
    /// in place, so two practitioners editing the same devis would otherwise silently overwrite each other's
    /// prices with no trace.
    /// </para>
    /// </summary>
    public uint Version { get; set; }

    public Guid Id { get; set; }
    public List<TreatmentPlanItemRequest> AddItems { get; set; } = new();

    /// <summary>
    /// Acts already on the plan to correct in place — designation, fee, teeth, catalog and procedure links.
    /// Each line's <c>Id</c> is required and must name an act on this plan; the act keeps that id, so every
    /// appointment and fiche link pointing at it survives the amendment.
    /// </summary>
    public List<TreatmentPlanItemRequest> UpdateItems { get; set; } = new();

    public List<Guid> RemoveItemIds { get; set; } = new();

    /// <summary>The devis title. Omitted or blank leaves it untouched (it is required on the aggregate).</summary>
    public string? Title { get; set; }

    private string? _notes;

    /// <summary>Explicit <c>null</c> clears the notes; omitting the key leaves them. Tri-state, as on
    /// <c>UpdateAppointmentCommand</c>: System.Text.Json only invokes the setter for a key that is physically
    /// present, so the setter doubles as a "was this sent?" probe.</summary>
    public string? Notes
    {
        get => _notes;
        set { _notes = value; NotesSpecified = true; }
    }

    /// <summary>
    /// The full replacement échéancier. May be omitted when the amendment leaves <c>TotalPlanned</c>
    /// unchanged; **required** when it changes, otherwise the schedule and the total would disagree and the
    /// two formulas the money reads use would stop matching.
    /// </summary>
    public List<InstallmentRequest> Installments { get; set; } = new();

    /// <summary>"Was this property present in the payload?" — not part of the wire contract in either
    /// direction, so a client can neither read nor forge it.</summary>
    [JsonIgnore] public bool NotesSpecified { get; private set; }
}

public class AmendTreatmentPlanCommandHandler : IRequestHandler<AmendTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AmendTreatmentPlanCommandHandler> _logger;

    public AmendTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        IAppointmentRepository appointmentRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<AmendTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _appointmentRepository = appointmentRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(AmendTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var retitling = !string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim() != plan.Title;
            var renoting = request.NotesSpecified && NormalizeNotes(request.Notes) != plan.Notes;

            if (request.AddItems.Count == 0
                && request.UpdateItems.Count == 0
                && request.RemoveItemIds.Count == 0
                && request.Installments.Count == 0
                && !retitling
                && !renoting)
            {
                return Result<TreatmentPlanDto>.Failure("Aucune modification demandée.");
            }

            /*
             * ⚠️ There is NO billed guard here any more, and its removal was a deliberate owner decision:
             * « le médecin doit pouvoir tout corriger ».
             *
             * What it used to refuse was amending a devis already represented by a note d'honoraires, on the
             * grounds that the note was raised from the devis' total and the two would then disagree. That
             * reasoning is sound about the *consequence* and wrong about the *remedy*: the dentist who mistyped
             * a fee is the one person who can fix it, and « annulez la facture d'abord » asks them to reverse a
             * numbered fiscal document to correct a plan.
             *
             * The divergence is therefore <b>surfaced rather than prevented</b>: `TreatmentPlanDto` carries the
             * linked note's own total, and the workspace states the gap and names the correction (an avoir, or a
             * corrective note). Nothing here silently re-writes an issued invoice — that is not ours to do.
             *
             * ⚠️ The money reads are unaffected either way: `GetPatientBillingSummaryQuery` drops a plan billed
             * into an invoice, so a changed `TotalPlanned` on such a plan moves no balance, no caisse figure and
             * no receivable. The gap is a *documentary* one, which is exactly why stating it is the whole fix.
             */


            var totalBefore = plan.TotalPlanned;

            // Title/notes first — they cannot fail on anything the act edits below decide, and doing them here
            // keeps the "nothing changed" check above and the mutation in the same order.
            if (retitling || renoting)
            {
                plan.UpdateDetails(
                    retitling ? request.Title!.Trim() : plan.Title,
                    renoting ? NormalizeNotes(request.Notes) : plan.Notes);
            }

            // In-place act edits before the removals and additions, so a line that is being corrected *and* a
            // line being removed in the same amendment cannot fight over the same id.
            if (request.UpdateItems.Count > 0)
            {
                var edits = await TreatmentPlanItemPricing.ResolveWithIdsAsync(
                    request.UpdateItems, clinicId, _procedureTypeRepository, cancellationToken);
                plan.UpdateItems(edits);

                /*
                 * The steps of an edited act, which `UpdateItems` cannot carry: `TreatmentPlanItem.Revise`
                 * deliberately touches the designation, fee, teeth and catalogue link only. Without this the
                 * whole step half of « Modifier le devis » was silently inert — a renamed séance, a re-ordered
                 * protocol or a deleted middle step all answered « Devis modifié » and changed nothing.
                 *
                 * Tri-state, like the creation path: a line that sends no `steps` key leaves the act's own
                 * sequence alone (the ordinary case, and what an older client sends), `[]` means « one séance »
                 * and a list is the new sequence. Ids are echoed back per step, so a step already carried out
                 * keeps its DoneDate and its fiche link.
                 */
                foreach (var line in request.UpdateItems.Where(i => i.Id.HasValue && i.Steps != null))
                {
                    plan.SetItemSteps(
                        line.Id!.Value,
                        line.Steps!.Select(s => new TreatmentPlanItemStepInput(
                            s.Id, s.Label, s.EstimatedDurationMinutes, s.MinDaysAfterPrevious)));
                }
            }

            // Removals next: taking an act out lowers the total, and doing it before the additions keeps
            // the appended acts' sequence numbers contiguous.
            if (request.RemoveItemIds.Count > 0)
            {
                var liveByItemId = await LiveAppointmentsByItemAsync(plan, clinicId, cancellationToken);
                foreach (var itemId in request.RemoveItemIds)
                {
                    liveByItemId.TryGetValue(itemId, out var liveAt);
                    plan.RemoveItem(itemId, liveAt);
                }
            }

            if (request.AddItems.Count > 0)
            {
                // Captured BEFORE the add, because `ApplyAsync` matches a confirmed list to an act by the act's
                // `SequenceNumber` — which on this path is its position in the whole plan, not in `AddItems`.
                var firstAddedPosition = plan.Items.Count == 0 ? 0 : plan.Items.Max(i => i.SequenceNumber) + 1;

                var items = await TreatmentPlanItemPricing.ResolveAsync(
                    request.AddItems, clinicId, _procedureTypeRepository, cancellationToken);
                plan.AddItems(items);

                /*
                 * An act added by amendment reaches a plan that is already Accepted, so it never passes through
                 * DevisNumbering — the other place the protocol is applied. Without this call, « ajouter une
                 * couronne » to a live devis produces the one act on the plan with no étape on it, and the
                 * dentist retypes the protocol by hand for exactly the acts that arrived latest.
                 * ApplyAsync only fills an act with no steps that is still Planned, so the acts already on the
                 * plan — including the half-finished bridge this amendment exists to bill — are untouched.
                 *
                 * ⚠️ The confirmed list is passed, as the creation path does. Omitting it discarded what the
                 * dentist had ticked: `steps: []` on a newly-added act — « je le fais en une séance » — was
                 * read as « the client did not decide » and the full catalogue protocol was applied over it.
                 */
                var confirmed = new List<IReadOnlyList<TreatmentPlanItemStepInput>?>();
                confirmed.AddRange(Enumerable.Repeat<IReadOnlyList<TreatmentPlanItemStepInput>?>(
                    null, firstAddedPosition));
                confirmed.AddRange(TreatmentPlanStepProtocol.ConfirmedByPosition(request.AddItems));

                await TreatmentPlanStepProtocol.ApplyAsync(
                    plan, clinicId, _procedureTypeRepository, cancellationToken, confirmed);
            }

            /*
             * A changed total must not leave the échéancier behind — Σ installment.Amount == TotalPlanned is
             * what keeps « Solde patient » and « Créances » reporting the same number.
             *
             * ⚠️ It used to REFUSE when no schedule came with the change, and that was the app fighting the
             * dentist: correcting a price from the booking dialog or the acts table has no échéancier on
             * screen to re-send, so the only honest answer there was « renvoyez l'échéancier », which names
             * something the caller cannot see. It re-spreads instead — every collected row stays at exactly
             * what it has taken and the balance lands on one row — so a price is editable from anywhere and
             * the invariant still holds. A plan with no schedule (an un-numbered treatment) is a no-op.
             */
            if (plan.TotalPlanned != totalBefore && request.Installments.Count == 0)
            {
                plan.RespreadScheduleToTotal(ClinicClock.ClinicToday());
            }

            if (request.Installments.Count > 0)
            {
                plan.ReviseInstallments(request.Installments.Select(i => (i.Id, i.DueDate, i.Amount)));
            }

            // One call, one revision — adding an act *and* re-spreading the échéancier is a single amendment
            // from the patient's point of view, and « révision N » only means something if it counts the
            // edits they could have been handed a printout of.
            plan.RecordAmendment();

            // Validate the save against the version the USER was editing, not the one this handler just
            // loaded — that one always matches and would detect nothing.
            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error amending treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la modification du devis.");
        }
    }

    /// <summary>
    /// Notes the way the aggregate stores them (blank → null, otherwise trimmed). Applied before comparing
    /// against <c>plan.Notes</c>, so re-submitting the form unchanged — which the amend dialog does every time,
    /// since it always sends both fields — is not mistaken for an edit and does not bump the revision.
    /// </summary>
    private static string? NormalizeNotes(string? notes)
        => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>
    /// A plan already represented by a real invoice cannot be amended. This is a **correctness** guard, not a
    /// convenience one: every money read counts such a plan through its invoice, and the invoice's lines froze
    /// at issue with no re-sync command anywhere — so acts added afterwards would be invisible in every
    /// balance. A silent undercount is exactly the bug class the unified money reads exist to prevent.
    /// The escape hatch already exists (cancel the invoice while unpaid, or issue an avoir once paid), and a
    /// plan whose only bridge is cancelled is amendable again.
    /// <para>
    /// Lives in the handler rather than the aggregate because <c>TreatmentPlan</c> holds no invoice reference.
    /// </para>
    /// </summary>
    private async Task<Result> EnsureNotBilledAsync(TreatmentPlan plan, Guid clinicId, CancellationToken cancellationToken)
    {
        var links = await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);
        var billed = PlanBillingRules.BilledPlanIds(links);

        return billed.Contains(plan.Id)
            ? Result.Failure("Ce devis est déjà facturé. Annulez la facture (ou émettez un avoir) avant de modifier le plan.")
            : Result.Success();
    }

    /// <summary>
    /// The still-standing appointment (if any) for each of the plan's acts, so <c>RemoveItem</c> can refuse to
    /// strand a patient who is booked for work that would no longer exist. One batched read for the whole plan.
    /// </summary>
    private async Task<Dictionary<Guid, DateTime?>> LiveAppointmentsByItemAsync(
        TreatmentPlan plan, Guid clinicId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var itemIds = plan.Items.Select(i => i.Id).ToList();
        var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
            new[] { plan }, clinicId, _appointmentRepository, _invoiceRepository, now, cancellationToken);

        // Only a *future* booking blocks removal — that is the case the guard is about: a patient still
        // expected, with reminders already sent, for work that would no longer exist. A past appointment is
        // history; its plan link simply stops resolving, which is harmless because the derivation runs
        // act → appointment, never the reverse. (A cancelled or no-show appointment is already excluded by
        // the projection, so cancelling the RDV is what unblocks the removal.)
        return itemIds.ToDictionary(
            id => id,
            id => workflow.ScheduledByItemId.TryGetValue(id, out var appointment)
                  && appointment.AppointmentDateTime >= now
                ? (DateTime?)appointment.AppointmentDateTime
                : null);
    }
}

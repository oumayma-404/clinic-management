using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A planned act line on a <see cref="TreatmentPlan"/> (aggregate child). References a catalog
/// <see cref="DentalActCode"/> (snapshotting its code) <b>or</b> carries a free-text designation for
/// non-CNAM acts (crowns, implants…). Optionally targets specific FDI teeth.
/// </summary>
public class TreatmentPlanItem : Entity<Guid>
{
    public Guid TreatmentPlanId { get; private set; }

    /// <summary>
    /// The clinic's own <see cref="ProcedureType"/> this act is performed as, when the line was chosen from
    /// that menu (null for a hand-typed line). A <b>soft reference</b> — deliberately no FK, like
    /// <see cref="LinkedDentalRecordId"/> — so retiring a procedure from the menu can never block or cascade
    /// into an existing devis.
    /// <para>
    /// ⚠️ <b>It is the only catalog a devis line comes from.</b> A line used to be able to carry a DCH
    /// (<c>DentalActCode</c>) reference instead, which is what fed the CNAM reimbursement split; that was
    /// removed deliberately — see <c>TreatmentPlanItemInput</c>.
    /// </para>
    /// <para>
    /// Carried so booking this act can preselect the procedure, which gives the appointment its colour and
    /// default duration and lets the dental-record modal propose the act when the visit is recorded. Before
    /// this existed the plan editor discarded the procedure's id and kept only its name, so a plan-scheduled
    /// appointment had no <c>ProcedureTypeId</c> at all.
    /// </para>
    /// </summary>
    public Guid? ProcedureTypeId { get; private set; }

    public string DesignationFr { get; private set; } = string.Empty;

    private readonly List<int> _toothNumbers = new();
    public IReadOnlyList<int> ToothNumbers => _toothNumbers.AsReadOnly();

    public decimal PlannedCost { get; private set; }
    public TreatmentPlanItemStatus Status { get; private set; }
    public DateTime? DoneDate { get; private set; }
    public Guid? LinkedDentalRecordId { get; private set; }

    /// <summary>
    /// Clinical order within the plan (0-based). Deliberately a plain ordering field, **not** a séance id:
    /// grouping acts into numbered séances is a separate, larger feature, and keeping this dumb means that
    /// change stays additive. Pre-migration rows read 0 and keep their insertion order until first reordered.
    /// </summary>
    public int SequenceNumber { get; private set; }

    private readonly List<TreatmentPlanItemStep> _steps = new();

    /// <summary>
    /// The act's clinical steps, in order — « Préparation », « Empreinte », « Scellement ». <b>Empty is the
    /// ordinary case</b> and means the act is done in one séance; every line written before steps existed has
    /// none, and behaves exactly as it did.
    /// <para>
    /// ⚠️ An unloaded collection navigation is <b>empty, not stale</b>, and there is no lazy loading in this
    /// solution — so a write path that forgets <c>.ThenInclude(i => i.Steps)</c> would silently treat a stepped
    /// act as step-less and overwrite its progress. <c>TreatmentStepLoadingCoverageTests</c> is the derived
    /// guard on that, and it is the reason <see cref="Status"/> is stored rather than computed on read.
    /// </para>
    /// </summary>
    public IReadOnlyList<TreatmentPlanItemStep> Steps =>
        _steps.OrderBy(s => s.SequenceNumber).ToList().AsReadOnly();

    /// <summary>Whether this act is carried out over several steps at all.</summary>
    public bool HasSteps => _steps.Count > 0;

    public int StepsTotal => _steps.Count;
    public int StepsDone => _steps.Count(s => s.IsDone);

    /// <summary>
    /// Whether any of this act's work has actually been delivered — the act is réalisé, or at least one of its
    /// steps carries a <c>DoneDate</c>.
    /// <para>
    /// ⚠️ <b>This, and never the act's derived workflow état, is what decides whether an act may be dropped.</b>
    /// « Arrêter le traitement » filtered on the état, which answers for the act's <i>next step</i> — so a bridge
    /// with two of three séances delivered reported « à planifier » and was offered for deletion, with its step
    /// rows and their fiche links. The one question a removal must ask is « has any of this happened? ».
    /// </para>
    /// </summary>
    public bool HasDeliveredWork =>
        Status == TreatmentPlanItemStatus.Done || _steps.Any(s => s.IsDone);

    /// <summary>Parked when the patient stopped — see <see cref="TreatmentPlanItemStatus.Withdrawn"/>.</summary>
    public bool IsWithdrawn => Status == TreatmentPlanItemStatus.Withdrawn;

    /// <summary>
    /// The date the act's next pending step should not be carried out before, from the interval it carries and
    /// the previous step's own date — or null when the act has no next step, no interval on it, or no delivered
    /// step to count from.
    /// </summary>
    /// <remarks>
    /// Lives on the act rather than on the step because only the act can see the pair. This is what lets a
    /// worklist distinguish « pas encore due » from « oubliée », instead of alarming on a flat fortnight for
    /// every protocol whatever its clinical rhythm.
    /// </remarks>
    public DateTime? NextStepDueFrom
    {
        get
        {
            var next = NextStep;
            if (next is null)
            {
                return null;
            }

            var previous = _steps
                .Where(s => s.IsDone && s.SequenceNumber < next.SequenceNumber)
                .OrderBy(s => s.SequenceNumber)
                .LastOrDefault();

            return next.DueFrom(previous?.DoneDate);
        }
    }

    /// <summary>The next step still to be carried out, or null when the act has none left (or none at all).</summary>
    public TreatmentPlanItemStep? NextStep =>
        _steps.Where(s => !s.IsDone).OrderBy(s => s.SequenceNumber).FirstOrDefault();

    private TreatmentPlanItem() { } // For EF Core

    public TreatmentPlanItem(
        Guid id,
        Guid treatmentPlanId,
        string designationFr,
        decimal plannedCost,
        IEnumerable<int>? toothNumbers = null,
        int sequenceNumber = 0,
        Guid? procedureTypeId = null)
    {
        if (string.IsNullOrWhiteSpace(designationFr))
            throw new ArgumentException("La désignation de l'acte est requise.", nameof(designationFr));
        if (plannedCost < 0)
            throw new ArgumentException("Le coût prévu ne peut pas être négatif.", nameof(plannedCost));

        Id = id;
        TreatmentPlanId = treatmentPlanId;
        ProcedureTypeId = procedureTypeId;
        DesignationFr = designationFr.Trim();
        PlannedCost = InvoiceCalculator.RoundMoney(plannedCost);
        Status = TreatmentPlanItemStatus.Planned;
        SequenceNumber = sequenceNumber;

        if (toothNumbers != null)
        {
            foreach (var tooth in toothNumbers.Distinct())
            {
                if (!FdiTooth.IsValid(tooth))
                    throw new ArgumentException($"Numéro de dent invalide : {tooth}.", nameof(toothNumbers));
                _toothNumbers.Add(tooth);
            }
        }
    }

    /// <summary>
    /// Correct **what this act is and what it costs**, in place, keeping its id.
    /// <para>
    /// Deliberately does not touch <see cref="Status"/>, <see cref="DoneDate"/>,
    /// <see cref="LinkedDentalRecordId"/> or <see cref="SequenceNumber"/>: a wrong price or a mistyped
    /// designation is a clerical correction, not a statement that the act un-happened or moved in the clinical
    /// order. Correcting whether it happened is <see cref="Unmark"/>; reordering is
    /// <c>TreatmentPlan.SetItemOrder</c>.
    /// </para>
    /// <para>
    /// Revising a <c>Done</c> act is allowed on purpose — that is the case this method mostly exists for. A
    /// price is very often noticed to be wrong only once the work is finished, and the fiche de soins that
    /// recorded the visit snapshots its own acts and costs, so the devis line and the clinical record cannot
    /// drift by editing this. What must *not* drift is the money, and that is guarded a level up: changing a
    /// cost changes <c>TotalPlanned</c>, which forces the échéancier to be resent and re-checked against
    /// <c>AmountPaid</c>.
    /// </para>
    /// </summary>
    public void Revise(
        string designationFr,
        decimal plannedCost,
        Guid? procedureTypeId,
        IEnumerable<int>? toothNumbers)
    {
        if (string.IsNullOrWhiteSpace(designationFr))
            throw new ArgumentException("La désignation de l'acte est requise.", nameof(designationFr));
        if (plannedCost < 0)
            throw new ArgumentException("Le coût prévu ne peut pas être négatif.", nameof(plannedCost));

        // Validate the whole tooth list before mutating anything — a half-applied revision would leave the act
        // with the new teeth and the old designation.
        var teeth = new List<int>();
        if (toothNumbers != null)
        {
            foreach (var tooth in toothNumbers.Distinct())
            {
                if (!FdiTooth.IsValid(tooth))
                    throw new ArgumentException($"Numéro de dent invalide : {tooth}.", nameof(toothNumbers));
                teeth.Add(tooth);
            }
        }

        DesignationFr = designationFr.Trim();
        PlannedCost = InvoiceCalculator.RoundMoney(plannedCost);
        ProcedureTypeId = procedureTypeId;
        _toothNumbers.Clear();
        _toothNumbers.AddRange(teeth);
    }

    /// <summary>
    /// Record that this act was carried out, linking the dental record that evidences it.
    /// <para>
    /// Re-marking is guarded. Editing the same record must stay idempotent (a fiche can be saved twice), so
    /// re-linking the <b>same</b> record is a no-op. But silently overwriting <see cref="DoneDate"/> and
    /// <see cref="LinkedDentalRecordId"/> with a <b>different</b> record would rewrite clinical history —
    /// the act would claim to have happened at a visit it did not — so that is refused.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ On an act that <b>has steps</b> this advances the <see cref="NextStep"/> rather than declaring the
    /// whole act finished, and the act reaches <c>Done</c> on its own when the last step lands. That is the
    /// truthful reading: the caller is a fiche de soins, and one fiche evidences one sitting. Marking every
    /// remaining step against a single record would claim the préparation happened at the scellement's visit.
    /// The step-targeted entry point is <c>TreatmentPlan.MarkItemStepDone</c>; this one is what the existing
    /// callers keep using when the séance named no particular step.
    /// </remarks>
    public void MarkDone(DateTime doneOn, Guid? linkedDentalRecordId)
    {
        EnsureNotWithdrawn();
        var next = NextStep;
        if (next != null)
        {
            MarkStepDone(next.Id, doneOn, linkedDentalRecordId);
            return;
        }

        if (Status == TreatmentPlanItemStatus.Done)
        {
            // Same evidence (or none supplied now): nothing changes, and re-saving a fiche must not fail.
            if (linkedDentalRecordId == null || linkedDentalRecordId == LinkedDentalRecordId)
            {
                return;
            }

            throw new InvalidOperationException(
                "Cet acte est déjà réalisé et rattaché à une autre fiche de soins. Détachez-le de cette fiche avant de le rattacher à une nouvelle.");
        }

        Status = TreatmentPlanItemStatus.Done;
        DoneDate = doneOn;
        LinkedDentalRecordId = linkedDentalRecordId;
    }

    /// <summary>
    /// Undo <see cref="MarkDone"/>: the act returns to « prévu » and its evidence link is cleared.
    /// <para>
    /// This is the "détachez-le de cette fiche" that <see cref="MarkDone"/> has always told the user to do and
    /// that had no implementation anywhere in the domain, application, API or UI. Without it, one act ticked
    /// against the wrong fiche was permanent — and because marking the last act done auto-completes the plan,
    /// it closed the whole devis with it.
    /// </para>
    /// <para>
    /// Returns <c>false</c> when the act was already « prévu », so the caller can distinguish "nothing to undo"
    /// from a real correction rather than silently re-opening a plan that never closed.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ On an act that <b>has steps</b> this undoes the <b>last</b> step carried out — the exact inverse of
    /// what <see cref="MarkDone"/> does to such an act. Un-marking the whole act in one call would erase the
    /// evidence links of steps whose fiches are untouched.
    /// </remarks>
    public bool Unmark()
    {
        if (HasSteps)
        {
            var lastDone = _steps.Where(s => s.IsDone).OrderBy(s => s.SequenceNumber).LastOrDefault();
            if (lastDone == null || !lastDone.Unmark())
            {
                return false;
            }

            RecomputeStatusFromSteps();
            return true;
        }

        if (Status != TreatmentPlanItemStatus.Done)
        {
            return false;
        }

        Status = TreatmentPlanItemStatus.Planned;
        DoneDate = null;
        LinkedDentalRecordId = null;
        return true;
    }

    /// <summary>
    /// Park this act because the patient stopped the treatment: it leaves the planned total and every progress
    /// count, and keeps its steps, their dates and their fiche links.
    /// <para>
    /// The alternative was removing it, and that is what destroyed delivered work — see
    /// <see cref="TreatmentPlanItemStatus.Withdrawn"/>. Idempotent, so a retried stop cannot fail halfway.
    /// </para>
    /// </summary>
    internal void Withdraw()
    {
        if (IsWithdrawn)
        {
            return;
        }

        Status = TreatmentPlanItemStatus.Withdrawn;
    }

    /// <summary>
    /// Put a parked act back into the treatment, at whatever état its own steps derive — so an act parked with
    /// two of three séances delivered returns « en cours », not « à planifier ».
    /// </summary>
    /// <returns><c>false</c> when the act was not parked, so the caller can tell a restore from a no-op.</returns>
    internal bool Restore()
    {
        if (!IsWithdrawn)
        {
            return false;
        }

        // Deliberately re-derived rather than remembered: the act's stored état before parking is exactly what
        // `RecomputeStatusFromSteps` computes, so storing it would be a second copy free to disagree.
        if (HasSteps)
        {
            Status = TreatmentPlanItemStatus.Planned;
            RecomputeStatusFromSteps();
            return true;
        }

        Status = DoneDate.HasValue ? TreatmentPlanItemStatus.Done : TreatmentPlanItemStatus.Planned;
        return true;
    }

    /// <summary>Place this act at a given position in the plan's clinical order.</summary>
    public void SetSequenceNumber(int sequenceNumber)
    {
        if (sequenceNumber < 0)
            throw new ArgumentException("La position de l'acte ne peut pas être négative.", nameof(sequenceNumber));

        SequenceNumber = sequenceNumber;
    }

    // ---- Steps ------------------------------------------------------------------------------------------

    /// <summary>
    /// Replace the act's whole step list, in the order given.
    /// <para>
    /// Replace rather than add/remove, for <c>TreatmentPlan.SetItems</c>' reason: the editor posts the list it
    /// is showing. But unlike that method the rows are <b>reused</b> when the caller echoes an
    /// <see cref="TreatmentPlanItemStepInput.Id"/> back, because a step accumulates state nothing else holds —
    /// its <see cref="TreatmentPlanItemStep.DoneDate"/>, its evidence link, and every
    /// <c>AppointmentProcedure.TreatmentPlanItemStepId</c> already pointing at it.
    /// </para>
    /// <para>
    /// A step already carried out may not be dropped — that is <see cref="Unmark"/>'s job, and silently
    /// deleting it would discard the only link back to the fiche that evidences it.
    /// </para>
    /// </summary>
    internal void SetSteps(IEnumerable<TreatmentPlanItemStepInput> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var requested = steps.ToList();
        if (requested.Count > TreatmentPlanItemStep.MaxStepsPerItem)
        {
            throw new InvalidOperationException(
                $"Un acte ne peut pas comporter plus de {TreatmentPlanItemStep.MaxStepsPerItem} étapes.");
        }

        // Cutting a finished act into steps would leave the recompute below with nothing to derive « réalisé »
        // from, so it would reopen the act and drop the fiche link that evidenced it.
        if (!HasSteps && Status == TreatmentPlanItemStatus.Done && requested.Count > 0)
        {
            throw new InvalidOperationException(
                "Un acte déjà réalisé ne peut pas être découpé en étapes. Détachez-le de sa fiche de soins d'abord.");
        }

        var echoed = requested.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToList();
        if (echoed.Distinct().Count() != echoed.Count)
        {
            throw new InvalidOperationException("La même étape ne peut pas figurer deux fois dans la liste.");
        }
        if (echoed.Any(id => _steps.All(s => s.Id != id)))
        {
            throw new InvalidOperationException("Étape introuvable.");
        }

        var kept = echoed.ToHashSet();
        var droppedDone = _steps.Where(s => s.IsDone && !kept.Contains(s.Id)).Select(s => s.Label).ToList();
        if (droppedDone.Count > 0)
        {
            throw new InvalidOperationException(
                $"L'étape « {droppedDone[0]} » est déjà réalisée et ne peut pas être retirée.");
        }

        // Build the whole list before mutating: a half-applied edit would leave the act with some new steps and
        // a status derived from the old ones.
        var rebuilt = new List<TreatmentPlanItemStep>();
        for (var position = 0; position < requested.Count; position++)
        {
            var input = requested[position];
            if (input.Id.HasValue)
            {
                var existing = _steps.First(s => s.Id == input.Id.Value);
                existing.Revise(input.Label, input.EstimatedDurationMinutes, input.MinDaysAfterPrevious);
                existing.SetSequenceNumber(position);
                rebuilt.Add(existing);
            }
            else
            {
                rebuilt.Add(new TreatmentPlanItemStep(
                    Guid.NewGuid(),
                    Id,
                    input.Label,
                    position,
                    input.EstimatedDurationMinutes,
                    input.MinDaysAfterPrevious));
            }
        }

        _steps.Clear();
        _steps.AddRange(rebuilt);
        RecomputeStatusFromSteps();
    }

    /// <summary>Record that one named step was carried out. Routed through <c>TreatmentPlan.MarkItemStepDone</c>,
    /// which owns the plan-level promotion this cannot see.</summary>
    internal void MarkStepDone(Guid stepId, DateTime doneOn, Guid? linkedDentalRecordId)
    {
        EnsureNotWithdrawn();
        var step = _steps.FirstOrDefault(s => s.Id == stepId)
            ?? throw new InvalidOperationException("Étape introuvable.");

        step.MarkDone(doneOn, linkedDentalRecordId);
        RecomputeStatusFromSteps();
    }

    /// <summary>Undo one named step. Returns <c>false</c> when it was already « à venir ».</summary>
    internal bool UnmarkStep(Guid stepId)
    {
        var step = _steps.FirstOrDefault(s => s.Id == stepId)
            ?? throw new InvalidOperationException("Étape introuvable.");

        if (!step.Unmark())
        {
            return false;
        }

        RecomputeStatusFromSteps();
        return true;
    }

    /// <summary>
    /// Bring <see cref="Status"/>, <see cref="DoneDate"/> and <see cref="LinkedDentalRecordId"/> back into
    /// agreement with the step rows. Same shape as <c>Installment.RecomputeFromLedger</c> and
    /// <c>Invoice.RecomputeCollected</c>: the scalar is stored so a query can filter on it, and it is only ever
    /// recomputed from the rows, never incremented.
    /// <para>
    /// A step-less act returns immediately — its stored values are authoritative and are what
    /// <see cref="MarkDone"/> / <see cref="Unmark"/> maintain.
    /// </para>
    /// <para>
    /// The act's own <see cref="LinkedDentalRecordId"/> becomes the <b>last</b> step's, i.e. "the fiche that
    /// finished it", so every existing reader of that field stays correct with no change.
    /// </para>
    /// </summary>
    private void EnsureNotWithdrawn()
    {
        if (IsWithdrawn)
        {
            throw new InvalidOperationException(
                $"L'acte « {DesignationFr} » a été retiré du traitement. Reprenez le traitement avant d'enregistrer du travail dessus.");
        }
    }

    private void RecomputeStatusFromSteps()
    {
        // A parked act derives nothing from its steps — that is what parking means, and letting the recompute
        // run would promote it back into the treatment the moment anything touched its list.
        if (!HasSteps || IsWithdrawn)
        {
            return;
        }

        var done = _steps.Count(s => s.IsDone);
        if (done == _steps.Count)
        {
            var last = _steps.OrderBy(s => s.SequenceNumber).Last();
            Status = TreatmentPlanItemStatus.Done;
            DoneDate = last.DoneDate;
            LinkedDentalRecordId = last.LinkedDentalRecordId;
            return;
        }

        Status = done == 0 ? TreatmentPlanItemStatus.Planned : TreatmentPlanItemStatus.InProgress;
        DoneDate = null;
        LinkedDentalRecordId = null;
    }
}

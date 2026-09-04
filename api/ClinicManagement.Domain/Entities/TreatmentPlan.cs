using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A dental treatment plan (aggregate root, clinic-scoped) — the spine of the dental core. A Draft holds
/// planned act lines + an optional installment schedule (échéancier) and renders as a devis (quote). It is
/// <c>Accept</c>ed to receive a per-clinic-per-year number (<c>AAAA-NNNN</c>, separate from invoices) and
/// freeze; acts are then marked done and installment payments recorded, moving it through
/// InProgress → Completed, or it can be Cancelled. Not a fiscal document (no VAT, no timbre).
/// </summary>
public class TreatmentPlan : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid PatientId { get; private set; }
    /// <summary>
    /// Which practitioner earned this — nullable, and nullable means nullable (L9 attribution).
    ///
    /// <para><b>What was missing.</b> <c>DoctorId</c> existed on exactly three entities in the whole model
    /// (<c>Appointment</c> — the only real FK to <c>Doctors</c> — <c>RecurringAppointment</c>, and
    /// <c>WaitingListEntry.PreferredDoctorId</c>, which was not even an FK), and on nothing that carries money or
    /// clinical work. So « combien a produit ce praticien ce mois ? » had no answer, and
    /// <c>Features/Dashboard/</c> contained <b>zero</b> occurrences of <c>Doctor</c> across all four readers.</para>
    ///
    /// <para>⚠️ <b>Historical rows legitimately have none</b> — the column did not exist when they were written,
    /// and the migration only backfills where a linked appointment names a practitioner. Every read must therefore
    /// tolerate null rather than treating it as « the clinic », which would silently attribute one dentist's work
    /// to whoever the filter happens to select.</para>
    ///
    /// <para>This is <b>attribution, not authorization</b>: it answers who earned a figure. Per-practitioner data
    /// scoping (« this dentist sees only their own patients ») is a separate decision with its own blast radius and
    /// is deliberately out of scope.</para>
    /// </summary>
    public Guid? DoctorId { get; private set; }

    /// <summary>The practitioner navigation, for the read-side name resolution. Null when unattributed.</summary>
    public Doctor? Doctor { get; private set; }

    /// <summary>
    /// Attribute (or un-attribute) this record to a practitioner. Deliberately its own mutator rather than a ctor
    /// parameter on every construction path: the answer is often only known *after* the aggregate exists (it comes
    /// from the appointment the record was written against), and a required ctor argument would have forced every
    /// caller to guess.
    /// </summary>
    public void SetDoctor(Guid? doctorId)
    {
        DoctorId = doctorId == Guid.Empty ? null : doctorId;
        Touch();
    }


    /// <summary>Sequential number <c>AAAA-NNNN</c>; null while a draft (assigned at acceptance).</summary>
    public string? Number { get; private set; }
    public TreatmentPlanStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTime? AcceptedDate { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>Sum of the planned act costs (TND millimes) — the devis total.</summary>
    public decimal TotalPlanned { get; private set; }

    /// <summary>
    /// How many times this devis has been amended since acceptance (0 = never). The devis PDF and the
    /// workspace header print « · révision N » when &gt; 0, so a patient holding an earlier printout can tell
    /// which version they signed — the PDF re-renders live from current state and is archived nowhere, so
    /// this counter is the only thing distinguishing two printouts of the same number. The
    /// <see cref="Number"/> itself is never reused, suffixed or reassigned.
    /// </summary>
    public int RevisionNumber { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<TreatmentPlanItem> _items = new();

    /// <summary>
    /// The planned acts in clinical order. <c>OrderBy</c> is a stable sort, so acts sharing a
    /// <see cref="TreatmentPlanItem.SequenceNumber"/> — every act on a plan created before the column
    /// existed, all of which default to 0 — keep their insertion order and do not reshuffle on screen before
    /// the plan is first reordered.
    /// </summary>
    public IReadOnlyCollection<TreatmentPlanItem> Items =>
        _items.OrderBy(i => i.SequenceNumber).ToList().AsReadOnly();

    private readonly List<Installment> _installments = new();
    public IReadOnlyCollection<Installment> Installments => _installments.AsReadOnly();

    public decimal AmountPaid => InvoiceCalculator.RoundMoney(_installments.Sum(i => i.AmountPaid));
    public decimal Outstanding => Math.Max(0m, TotalPlanned - AmountPaid);
    public bool CanBeDeleted => Status == TreatmentPlanStatus.Draft;

    private TreatmentPlan() { } // For EF Core

    public TreatmentPlan(Guid id, Guid clinicId, Guid patientId, string title, string? notes = null)
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Le titre du plan est requis.", nameof(title));

        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        Title = title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = TreatmentPlanStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        RecomputeTotal();
    }

    /// <summary>
    /// Update the title / notes.
    /// <para>
    /// Allowed on a draft <b>and</b> on an accepted or in-progress devis. It used to be <c>EnsureDraft</c>-only,
    /// which meant a typo in the title a patient reads on their devis printout froze permanently at acceptance
    /// and the only way to fix it was to cancel the devis and retype it — losing its number. Neither field is
    /// money and neither is the number itself, and an amendment that changes the printed title bumps
    /// <see cref="RevisionNumber"/> through the same <see cref="RecordAmendment"/> its caller already calls, so
    /// an earlier printout stays identifiable. Refused on a Completed or Cancelled plan, matching the two
    /// windows its callers live in (the draft editor and <c>EnsureAmendable</c>).
    /// </para>
    /// </summary>
    public void UpdateDetails(string title, string? notes)
    {
        if (Status != TreatmentPlanStatus.Draft
            && Status != TreatmentPlanStatus.Accepted
            && Status != TreatmentPlanStatus.InProgress)
        {
            throw new InvalidOperationException("Seul un devis brouillon, accepté ou en cours peut être modifié.");
        }
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Le titre du plan est requis.", nameof(title));

        Title = title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch();
    }

    /// <summary>Replace all planned act lines. Draft only. Clears any installment schedule (totals change).</summary>
    public void SetItems(IEnumerable<(string designationFr, decimal plannedCost, IReadOnlyList<int> toothNumbers)> items)
        => SetItems(items.Select(i => new TreatmentPlanItemInput(
            null, i.designationFr, i.plannedCost, null, i.toothNumbers)));

    /// <summary>
    /// Tuple adapter kept for callers that predate <see cref="TreatmentPlanItemInput"/>. Lines set no
    /// <c>ProcedureTypeId</c> — correct, since a caller that cannot express one has not chosen a procedure.
    /// </summary>
    public void SetItems(
        IEnumerable<(Guid? id, string designationFr, decimal plannedCost, IReadOnlyList<int> toothNumbers)> items,
        bool scheduleWillBeResent = true)
        => SetItems(
            items.Select(i => new TreatmentPlanItemInput(
                i.id, i.designationFr, i.plannedCost, null, i.toothNumbers)),
            scheduleWillBeResent);

    /// <summary>
    /// Replace all planned act lines, **preserving the id** of every echoed-back line. Draft only.
    /// <para>
    /// Id preservation is not a micro-optimisation. Editing a draft used to <c>Guid.NewGuid()</c> every line,
    /// so an <c>Appointment.TreatmentPlanItemId</c> or <c>TreatmentPlanItem.LinkedDentalRecordId</c> pointing
    /// at that act silently began pointing at nothing — and since neither link has an FK, the database could
    /// not catch it. A line whose id the caller echoes back keeps it; an unknown id is treated as a new line
    /// rather than an error, so a stale client cannot fail the save.
    /// </para>
    /// <para>
    /// Wiping the échéancier is now **explicit**: this still clears it (the total is changing), but it
    /// refuses to do so silently when a schedule exists and the caller sent no replacement — previously the
    /// only reason no money was lost is that the form always happened to resend the schedule.
    /// </para>
    /// </summary>
    public void SetItems(
        IEnumerable<TreatmentPlanItemInput> items,
        bool scheduleWillBeResent = true)
    {
        EnsureDraft();

        if (_installments.Any(i => i.AmountPaid > 0m))
        {
            // Defensive: a Draft cannot take payments today (EnsurePayable rejects Draft), so this can only
            // fire if that guard ever loosens. Losing collected money to a line edit must never be possible.
            throw new InvalidOperationException(
                "Ce devis comporte des échéances déjà encaissées et ne peut plus être modifié ligne par ligne.");
        }
        if (_installments.Count > 0 && !scheduleWillBeResent)
        {
            throw new InvalidOperationException(
                "Modifier les actes change le total du devis : renvoyez l'échéancier avec la mise à jour.");
        }

        var existingById = _items.ToDictionary(i => i.Id);
        var rebuilt = new List<TreatmentPlanItem>();
        var position = 0;

        foreach (var item in items)
        {
            // An echoed-back id that still exists on this plan keeps its identity, so every link to that act
            // survives the edit. Anything else is a new line.
            var reusedId = item.Id.HasValue && existingById.ContainsKey(item.Id.Value) ? item.Id.Value : Guid.NewGuid();
            rebuilt.Add(new TreatmentPlanItem(
                reusedId,
                Id,
                item.DesignationFr,
                item.PlannedCost,
                item.ToothNumbers,
                position,
                item.ProcedureTypeId));
            position++;
        }

        _items.Clear();
        _items.AddRange(rebuilt);
        _installments.Clear();
        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// Replace the installment schedule (échéancier). Draft only. If any installments are given, their
    /// amounts must sum exactly to the total planned cost (the caller lands the millime remainder on the
    /// last installment). An empty schedule is allowed (no formal plan; then no installment payments).
    /// </summary>
    public void SetInstallments(IEnumerable<(DateTime dueDate, decimal amount)> installments)
    {
        EnsureDraft();
        var list = installments.ToList();
        _installments.Clear();
        if (list.Count == 0)
        {
            Touch();
            return;
        }

        var sum = InvoiceCalculator.RoundMoney(list.Sum(i => i.amount));
        if (sum != TotalPlanned)
            throw new InvalidOperationException("Le total des échéances doit être égal au coût total planifié du devis.");

        foreach (var (dueDate, amount) in list)
        {
            _installments.Add(new Installment(Guid.NewGuid(), Id, dueDate, amount));
        }
        Touch();
    }

    /// <summary>
    /// Accept the devis: assign its (externally computed, unique) sequential number and move it to Accepted.
    /// Requires at least one act.
    /// </summary>
    public void Accept(string number)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro du devis est requis.", nameof(number));
        if (_items.Count == 0)
            throw new InvalidOperationException("Un plan doit comporter au moins un acte pour être accepté.");

        Number = number.Trim();
        AcceptedDate = DateTime.UtcNow;
        Status = TreatmentPlanStatus.Accepted;

        // Ensure the accepted plan is payable: a devis with no échéancier gets a single lump-sum installment
        // for the full planned total, due at acceptance — otherwise Outstanding (derived from installments)
        // would be stuck at the total forever with no way to record a payment.
        if (_installments.Count == 0 && TotalPlanned > 0m)
        {
            _installments.Add(new Installment(Guid.NewGuid(), Id, AcceptedDate.Value, TotalPlanned));
        }

        Touch();
    }

    /// <summary>Reassign the number on an accepted plan — only to resolve a concurrent numbering collision.</summary>
    public void SetAcceptedNumber(string number)
    {
        if (Status == TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Le numéro ne peut être attribué qu'à un plan accepté.");
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro du devis est requis.", nameof(number));

        Number = number.Trim();
        Touch();
    }

    /// <summary>
    /// Record a payment against one installment. Allowed on an accepted, in-progress <b>or completed</b> plan —
    /// see <see cref="EnsurePayable"/>. A payment never re-opens a completed plan.
    /// </summary>
    /// <param name="cheque">The cheque's number, bank and due date (L8). Null for any other method.</param>
    public InstallmentPayment RecordInstallmentPayment(
        Guid installmentId,
        decimal amount,
        PaymentMethod method,
        DateTime paidOn,
        ChequeDetails? cheque = null)
    {
        EnsurePayable();
        var installment = _installments.FirstOrDefault(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("Échéance introuvable.");

        var payment = installment.RecordPayment(amount, method, paidOn, cheque);
        if (Status == TreatmentPlanStatus.Accepted)
            Status = TreatmentPlanStatus.InProgress;
        Touch();
        return payment;
    }

    /// <summary>
    /// Void a payment recorded against one of this plan's échéances — "this was never received".
    ///
    /// <para>
    /// The plan's <b>status is deliberately not walked back</b>, unlike an invoice's. A plan's status tracks
    /// clinical progress (« Terminé » means every act is done, not that it is paid), so a corrected payment
    /// must not un-start or un-complete the treatment.
    /// </para>
    /// </summary>
    public void VoidInstallmentPayment(
        Guid installmentId,
        Guid paymentId,
        string reason,
        string? actorUserId = null,
        string? actorName = null)
    {
        if (Status == TreatmentPlanStatus.Cancelled)
            throw new InvalidOperationException("Ce devis est annulé : ses paiements ne peuvent plus être modifiés.");

        var installment = _installments.FirstOrDefault(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("Échéance introuvable.");

        installment.VoidPayment(paymentId, reason, actorUserId, actorName);
        Touch();
    }

    /// <inheritdoc cref="Invoice.SetPaymentBanked"/>
    /// <remarks>
    /// Reachable on a <b>cancelled</b> devis too, unlike <see cref="VoidInstallmentPayment"/>: cancelling a plan
    /// does not hand back a cheque the patient already wrote, and a cheque that still has to be banked — or has
    /// just bounced — is exactly the row that must stay correctable.
    /// </remarks>
    public void SetInstallmentPaymentBanked(
        Guid installmentId,
        Guid paymentId,
        bool banked,
        string? actorUserId = null,
        string? actorName = null)
    {
        var installment = _installments.FirstOrDefault(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("Échéance introuvable.");

        installment.SetPaymentBanked(paymentId, banked, actorUserId, actorName);
        Touch();
    }

    /// <summary>
    /// Mark a planned act as carried out, optionally linking the dental record that recorded it. When this was
    /// the last outstanding act the plan closes itself.
    /// <para>
    /// The auto-close rule lives here, not in a handler, so every path behaves identically: previously only
    /// <c>MarkTreatmentPlanItemDoneCommand</c> auto-closed while the record-driven path
    /// (<c>DentalRecordLinker</c>) did not — and since that command has no UI caller, a fully-treated plan
    /// never actually reached « Terminé » on its own.
    /// </para>
    /// </summary>
    public void MarkItemDone(Guid itemId, DateTime doneOn, Guid? linkedDentalRecordId)
    {
        EnsureActive();
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.MarkDone(doneOn, linkedDentalRecordId);
        if (Status == TreatmentPlanStatus.Accepted)
            Status = TreatmentPlanStatus.InProgress;

        // EnsureActive + the bump above leave the plan InProgress, so Complete() can never throw here.
        if (ActiveItems.Any() && ActiveItems.All(i => i.Status == TreatmentPlanItemStatus.Done))
        {
            Complete();
        }

        Touch();
    }

    /// <summary>Close the plan once every act has been carried out.</summary>
    /// <summary>
    /// Undo <see cref="MarkItemDone"/> for one act, reopening the plan as the exact inverse of the promotions
    /// that method performs.
    /// <para>
    /// Deliberately <b>not</b> guarded by <see cref="EnsureActive"/>: marking the last act done auto-completes
    /// the plan, so a correction that required an active plan could never reach the case it exists for. One act
    /// ticked against the wrong fiche would close a devis permanently.
    /// </para>
    /// <para>
    /// The caller is responsible for refusing an act already billed on a live invoice — the domain cannot see
    /// invoices, and un-marking billed work would desynchronise the plan from the money.
    /// </para>
    /// </summary>
    public void UnmarkItemDone(Guid itemId)
    {
        EnsureCorrectable();
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        if (!item.Unmark())
        {
            // Already « prévu » — do not touch the plan's status; it was never closed by this act.
            return;
        }

        // Mirror MarkItemDone exactly: it promotes Accepted → InProgress on the first done act and → Completed
        // when all are done. Completed is therefore only reachable with every act done, so un-marking one always
        // reopens; and with no act done at all the plan is back where acceptance left it.
        Status = AnyWorkRecorded ? TreatmentPlanStatus.InProgress : TreatmentPlanStatus.Accepted;

        Touch();
    }

    /// <summary>
    /// Record that one <b>step</b> of a planned act was carried out, linking the fiche that evidences it. The
    /// act reaches « réalisé » on its own once its last step lands, and the plan closes itself once that was
    /// the last outstanding act — so this is <see cref="MarkItemDone"/>'s promotion chain, entered one step
    /// lower.
    /// <para>
    /// This is the entry point a fiche de soins uses when the séance named a step. A séance that named none
    /// still goes through <see cref="MarkItemDone"/>, which advances the next step for a stepped act.
    /// </para>
    /// </summary>
    public void MarkItemStepDone(Guid itemId, Guid stepId, DateTime doneOn, Guid? linkedDentalRecordId)
    {
        EnsureActive();
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.MarkStepDone(stepId, doneOn, linkedDentalRecordId);
        if (Status == TreatmentPlanStatus.Accepted)
            Status = TreatmentPlanStatus.InProgress;

        // EnsureActive + the bump above leave the plan InProgress, so Complete() can never throw here.
        if (ActiveItems.Any() && ActiveItems.All(i => i.Status == TreatmentPlanItemStatus.Done))
        {
            Complete();
        }

        Touch();
    }

    /// <summary>
    /// Undo <see cref="MarkItemStepDone"/> for one step — the correction path, and the implementation of the
    /// « détachez-la de cette fiche » that <c>TreatmentPlanItemStep.MarkDone</c> tells the user to do.
    /// <para>
    /// <see cref="EnsureCorrectable"/> rather than <see cref="EnsureActive"/>, for <see cref="UnmarkItemDone"/>'s
    /// reason: the last step landing closes the whole devis, so a gate that required an active plan could never
    /// reach the mistake it exists to fix.
    /// </para>
    /// <para>
    /// Returns <c>false</c> when the step was already « à venir », so the caller can distinguish "nothing to
    /// undo" from a real correction rather than silently reopening a plan that never closed.
    /// </para>
    /// </summary>
    public bool UnmarkItemStep(Guid itemId, Guid stepId)
    {
        EnsureCorrectable();
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        if (!item.UnmarkStep(stepId))
        {
            return false;
        }

        Status = AnyWorkRecorded ? TreatmentPlanStatus.InProgress : TreatmentPlanStatus.Accepted;
        Touch();
        return true;
    }

    /// <summary>
    /// Set the clinical steps of one planned act — « Préparation, Empreinte, Scellement ».
    /// <para>
    /// Gated like <see cref="SetItemOrder"/> rather than like <see cref="AddItems"/>, and the difference is the
    /// point: <b>no money moves</b>. The act's <c>PlannedCost</c>, the devis total and the échéancier are all
    /// untouched, so this does not bump <see cref="RevisionNumber"/> (nothing the patient signed for changes)
    /// and it stays available on a plan whose facture is already issued — a dentist must be able to correct the
    /// protocol of a bridge he is halfway through, and <see cref="EnsureAmendable"/> would refuse exactly that
    /// on a billed or completed plan.
    /// </para>
    /// <para>
    /// Refused on a cancelled plan only. Editing steps can still change the act's <c>Status</c> (adding a step
    /// to a finished act reopens it), so the plan's own status is re-derived here the same way
    /// <see cref="UnmarkItemStep"/> does it.
    /// </para>
    /// </summary>
    public void SetItemSteps(Guid itemId, IEnumerable<TreatmentPlanItemStepInput> steps)
    {
        if (Status == TreatmentPlanStatus.Cancelled)
            throw new InvalidOperationException("Les étapes d'un plan annulé ne peuvent pas être modifiées.");
        if (Status == TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Le devis doit être accepté pour définir les étapes d'un acte.");

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.SetSteps(steps);

        // A Completed plan that gains a step is no longer finished, and one whose every act is done again is.
        Status = ActiveItems.Any() && ActiveItems.All(i => i.Status == TreatmentPlanItemStatus.Done)
            ? TreatmentPlanStatus.Completed
            : AnyWorkRecorded ? TreatmentPlanStatus.InProgress : TreatmentPlanStatus.Accepted;

        Touch();
    }

    /// <summary>
    /// Whether any clinical work is recorded against this plan — an act « réalisé » <b>or</b> one « en cours »
    /// because some of its steps are done. Reading only <c>Done</c> here would walk a plan back to
    /// « Accepté » while a bridge sat half-finished on it.
    /// </summary>
    private bool AnyWorkRecorded => _items.Any(i => i.HasDeliveredWork);

    /// <summary>
    /// The acts that still count as this plan's treatment — everything except the ones parked by
    /// <see cref="StopTreatment"/>. Every total, progress count and « is it finished » test reads this rather
    /// than <see cref="Items"/>, so a parked act contributes nothing while keeping its history.
    /// </summary>
    public IEnumerable<TreatmentPlanItem> ActiveItems => _items.Where(i => !i.IsWithdrawn);

    /// <summary>
    /// Close the clinical side of the devis. Money is unaffected: « Terminé » means the work is over, not that
    /// the patient has paid, so the échéancier stays collectable (see <see cref="EnsurePayable"/>).
    /// </summary>
    /// <param name="leaveUnrealisedActs">
    /// Close even though acts are still « non réalisé », leaving them so — the promise the « Terminer » dialog
    /// has always made in words (« la clôture ne les valide pas ») and that this method used to refuse, making
    /// the button fail in precisely the case the dialog bothered to explain. Off by default, so the automatic
    /// clôture fired when the last step lands still asserts that everything really is done.
    /// </param>
    public void Complete(bool leaveUnrealisedActs = false)
    {
        if (Status != TreatmentPlanStatus.Accepted && Status != TreatmentPlanStatus.InProgress)
            throw new InvalidOperationException("Seul un plan accepté ou en cours peut être clôturé.");
        if (!leaveUnrealisedActs && ActiveItems.Any(i => i.Status != TreatmentPlanItemStatus.Done))
            throw new InvalidOperationException("Tous les actes doivent être réalisés avant de clôturer le plan.");

        Status = TreatmentPlanStatus.Completed;
        Touch();
    }

    /// <summary>
    /// The patient is not continuing: park every act that has no delivered work, keep the rest, re-spread the
    /// échéancier onto the kept total, and close the devis — <b>in one transition</b>.
    /// <para>
    /// ⚠️ <b>Three separate defects live in the shape this replaces</b>, which was two client calls (amend, then
    /// complete) driving <see cref="RemoveItem"/> off a client-side filter:
    /// </para>
    /// <list type="number">
    /// <item>The filter asked for the act's derived état, which answers for its <i>next step</i>, so a bridge
    /// with two of three séances delivered was offered for deletion. <see cref="TreatmentPlanItem.HasDeliveredWork"/>
    /// is the only question a drop may ask, and it is asked here rather than in a caller.</item>
    /// <item>The acts were <b>deleted</b>, taking their step rows and the links to the fiches that evidenced
    /// them, so two real séances survived attached to nothing and the treatment could never be resumed. They are
    /// parked now — see <see cref="TreatmentPlanItemStatus.Withdrawn"/> and <see cref="Reopen"/>.</item>
    /// <item>The two calls were not atomic: the clôture threw <i>after</i> the removals committed, leaving the
    /// acts gone, the échéancier rewritten and the plan still open, with no way to retry or finish.</item>
    /// </list>
    /// <para>
    /// A kept total of 0 clears the échéancier instead of writing a zero row — the aggregate refuses one
    /// (« Le montant de l'échéance doit être supérieur à 0 »), which is what made stopping an unpaid treatment a
    /// screen with no way out.
    /// </para>
    /// </summary>
    /// <param name="dueDate">When the re-spread balance is due. The caller supplies it from the clinic clock.</param>
    /// <returns>The acts parked, in clinical order — what the caller reports back.</returns>
    public IReadOnlyList<TreatmentPlanItem> StopTreatment(DateTime dueDate)
    {
        if (Status != TreatmentPlanStatus.Accepted && Status != TreatmentPlanStatus.InProgress)
        {
            throw new InvalidOperationException("Seul un traitement accepté ou en cours peut être arrêté.");
        }

        var parked = _items
            .Where(i => !i.IsWithdrawn && !i.HasDeliveredWork)
            .OrderBy(i => i.SequenceNumber)
            .ToList();
        var kept = _items.Where(i => !i.IsWithdrawn && i.HasDeliveredWork).ToList();

        if (kept.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucun acte de ce devis n'a été réalisé : annulez-le (un motif est requis) plutôt que d'arrêter le traitement.");
        }

        foreach (var item in parked)
        {
            item.Withdraw();
        }

        RecomputeTotal();

        if (TotalPlanned < AmountPaid)
        {
            throw new InvalidOperationException(
                $"{AmountPaid:0.000} DT ont déjà été encaissés sur ce devis, pour {TotalPlanned:0.000} DT d'actes conservés. "
                + "Remboursez la différence par un avoir avant d'arrêter le traitement.");
        }

        RespreadSchedule(dueDate);
        RevisionNumber++;
        Complete(leaveUnrealisedActs: true);
        return parked;
    }

    /// <summary>
    /// Put a stopped treatment back into service: the devis reopens and every parked act returns at the état its
    /// own steps derive, so a bridge parked two séances in comes back « en cours » rather than « à planifier ».
    /// <para>
    /// ⚠️ It exists because a stopped plan was a terminal state. « Arrêter » left it <c>Completed</c>, which
    /// withdraws « Arrêter », « Terminer », « Facturer » and « Annuler » alike — and the dropped acts had been
    /// deleted, so « Modifier le devis » could only re-type them as new ids, orphaning the fiches. Patients come
    /// back; the model has to expect it.
    /// </para>
    /// <para>
    /// The échéancier is <b>not</b> restored, deliberately: the parked acts return unrealised and re-pricing them
    /// is the amendment that follows, which re-spreads the schedule with the dentist looking at it.
    /// </para>
    /// </summary>
    public void Reopen()
    {
        if (Status != TreatmentPlanStatus.Completed)
        {
            throw new InvalidOperationException("Seul un devis terminé peut être repris.");
        }

        // `ToList()` first: `Restore` mutates, and counting a lazy sequence would restore only what is enumerated.
        var restored = _items.Select(i => i.Restore()).ToList().Count(r => r);
        Status = AnyWorkRecorded ? TreatmentPlanStatus.InProgress : TreatmentPlanStatus.Accepted;
        if (restored > 0)
        {
            RecomputeTotal();
            RevisionNumber++;
        }
        Touch();
    }

    /// <summary>
    /// Re-spread the balance onto one échéance after the total changed, keeping the rows that collected money.
    /// <para>
    /// Each collected row is trimmed to exactly what it took, so <c>Σ Amount == TotalPlanned</c> still holds —
    /// the invariant « Solde patient » and « Créances » agree only while it does (see
    /// <see cref="ReviseInstallments"/>). Nothing outstanding means <b>no row at all</b>: <c>Installment</c>
    /// refuses a zero amount, and writing one is what left « Arrêter le traitement » on an unpaid devis as a
    /// dialog answering « Le montant de l'échéance doit être supérieur à 0 » with no way forward.
    /// </para>
    /// </summary>
    private void RespreadSchedule(DateTime dueDate)
    {
        var outstanding = Outstanding;
        var collected = _installments.Where(i => i.AmountPaid > 0m).ToList();
        foreach (var row in collected)
        {
            row.Revise(row.DueDate, row.AmountPaid);
        }

        _installments.Clear();
        _installments.AddRange(collected);
        if (outstanding > 0m)
        {
            _installments.Add(new Installment(Guid.NewGuid(), Id, dueDate, outstanding));
        }
    }

    // ---- Amendment (post-acceptance) ---------------------------------------------------------------
    //
    // Before this, a plan froze the instant it was accepted: SetItems/SetInstallments are EnsureDraft()-only,
    // so the first time treatment changed the only escape was Cancel + retype, losing the devis number, the
    // échéancier and every réalisé act. These methods let an accepted plan evolve instead: add acts, revise the
    // ones already on it, remove them, re-spread the échéancier, reorder, and stamp the revision.
    //
    // The caller (the amend handler) is responsible for the one rule this aggregate cannot see: a plan with a
    // linked non-cancelled invoice must refuse every amendment, because the money reads treat that invoice as
    // *representing* the plan and its lines froze at issue — added acts would be silently invisible in every
    // balance. TreatmentPlan holds no invoice reference, so that guard lives in the handler with the
    // repository that can answer it.

    /// <summary>
    /// Add acts to an accepted or in-progress plan. New acts append after the current last one, so an
    /// amendment never reshuffles the clinical order the dentist already set. Bumps the revision.
    /// </summary>
    public void AddItems(IEnumerable<(string designationFr, decimal plannedCost, IReadOnlyList<int> toothNumbers)> items)
        => AddItems(items.Select(i => new TreatmentPlanItemInput(
            null, i.designationFr, i.plannedCost, null, i.toothNumbers)));

    /// <inheritdoc cref="AddItems(IEnumerable{ValueTuple{string, decimal, IReadOnlyList{int}}})"/>
    /// <remarks>
    /// Each line's <see cref="TreatmentPlanItemInput.Id"/> is ignored — an added act is always new. Its
    /// <see cref="TreatmentPlanItemInput.ProcedureTypeId"/> is kept, so an act appended by an amendment can be
    /// booked with its procedure preselected just like one that was in the original devis.
    /// </remarks>
    public void AddItems(IEnumerable<TreatmentPlanItemInput> items)
    {
        EnsureAmendable();

        var next = NextSequenceNumber();
        var added = 0;
        foreach (var item in items)
        {
            _items.Add(new TreatmentPlanItem(
                Guid.NewGuid(),
                Id,
                item.DesignationFr,
                item.PlannedCost,
                item.ToothNumbers,
                next,
                item.ProcedureTypeId));
            next++;
            added++;
        }

        if (added == 0)
        {
            return;
        }

        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// Correct acts **already on** an accepted or in-progress plan, in place — designation, fee, teeth, and the
    /// catalog/procedure links — keeping each act's id.
    /// <para>
    /// This is the third amendment verb, and its absence was the gap: <see cref="AddItems"/> and
    /// <see cref="RemoveItem"/> could only ever express "change this act" as remove-then-add, which re-issues
    /// the id (orphaning any appointment or fiche link pointing at it) and is refused outright for an act that
    /// is <c>Done</c> or booked. So the one correction a dentist actually needs most — a wrong price on work
    /// already scheduled or carried out — was the one the amendment window could not make.
    /// </para>
    /// <para>
    /// Each line's <see cref="TreatmentPlanItemInput.Id"/> must name an act on this plan; an unknown id is
    /// refused rather than silently added, because a caller asking to *revise* a specific act and getting a new
    /// one instead would double the line and the total. Recomputes <see cref="TotalPlanned"/>, so the caller
    /// carries the same obligation as after an add or a remove: a changed total must be followed by a
    /// re-spread échéancier.
    /// </para>
    /// </summary>
    public void UpdateItems(IEnumerable<TreatmentPlanItemInput> items)
    {
        EnsureAmendable();

        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        // Resolve every line before mutating any of them: a partially-applied batch would leave the plan with
        // some acts revised, some not, and a total that matches neither the old devis nor the new one.
        var targets = new List<(TreatmentPlanItem Item, TreatmentPlanItemInput Input)>();
        foreach (var input in list)
        {
            if (!input.Id.HasValue)
            {
                throw new InvalidOperationException("Un acte à modifier doit désigner l'acte existant concerné.");
            }

            var item = _items.FirstOrDefault(i => i.Id == input.Id.Value)
                ?? throw new InvalidOperationException("Acte introuvable.");
            targets.Add((item, input));
        }

        foreach (var (item, input) in targets)
        {
            item.Revise(
                input.DesignationFr,
                input.PlannedCost,
                input.ProcedureTypeId,
                input.ToothNumbers);
        }

        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// Remove an act from an accepted or in-progress plan and lower <see cref="TotalPlanned"/> accordingly.
    /// Bumps the revision.
    /// <para>
    /// Refused for an act already <c>Done</c>, and refused for an act the patient is still booked for —
    /// <paramref name="liveAppointmentAt"/> carries that booking (the aggregate cannot query appointments, so
    /// the caller supplies it). Removing a booked act would leave an appointment row pointing at a vanished
    /// id, with the patient still expected — reminders already sent — for work that no longer exists, and no
    /// FK to catch it. Whether to un-book the patient or repurpose the slot is a phone call, not a cascade.
    /// </para>
    /// </summary>
    public void RemoveItem(Guid itemId, DateTime? liveAppointmentAt = null)
    {
        EnsureAmendable();

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        if (item.Status == TreatmentPlanItemStatus.Done)
        {
            throw new InvalidOperationException("Cet acte est déjà réalisé et ne peut plus être retiré du devis.");
        }
        // ⚠️ An act part-way through is `InProgress`, not `Done`, so the test above let a bridge with two of
        // three séances delivered be deleted — with its step rows and the links to the two fiches that
        // evidenced them. Step-level removal has always refused this (« L'étape « X » est déjà réalisée et ne
        // peut pas être retirée »); the act level was the hole.
        if (item.HasDeliveredWork)
        {
            throw new InvalidOperationException(
                $"L'acte « {item.DesignationFr} » a déjà {item.StepsDone} séance(s) réalisée(s) et ne peut pas être retiré du devis. "
                + "Arrêtez le traitement pour le mettre de côté sans perdre ce qui a été fait.");
        }
        if (liveAppointmentAt.HasValue)
        {
            throw new InvalidOperationException(
                $"Cet acte a un rendez-vous prévu le {liveAppointmentAt.Value:dd/MM}. Annulez ou déplacez le rendez-vous avant de retirer l'acte.");
        }

        _items.Remove(item);
        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// Replace the échéancier on an accepted plan. An installment whose id is echoed back keeps its identity
    /// (and therefore its collected money); anything else is a new row.
    /// <para>
    /// The schedule must sum <b>exactly</b> to <see cref="TotalPlanned"/>. That invariant is load-bearing well
    /// beyond this method: « Solde patient » reads <c>plan.Outstanding</c> (<c>TotalPlanned − Σ AmountPaid</c>)
    /// while « Créances » and the dashboard read <c>Σ (Amount − AmountPaid)</c>, and the two agree only while
    /// it holds. Nothing enforced it before because nothing could change the total after acceptance.
    /// </para>
    /// <para>Bumps the revision. As with <c>SetInstallments</c>, the caller lands the millime remainder.</para>
    /// </summary>
    public void ReviseInstallments(IEnumerable<(Guid? id, DateTime dueDate, decimal amount)> installments)
    {
        EnsureAmendable();

        var list = installments.ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException("L'échéancier ne peut pas être vide sur un devis accepté.");
        }

        var sum = InvoiceCalculator.RoundMoney(list.Sum(i => i.amount));
        if (sum != TotalPlanned)
        {
            throw new InvalidOperationException("Le total des échéances doit être égal au coût total planifié du devis.");
        }
        if (TotalPlanned < AmountPaid)
        {
            throw new InvalidOperationException(
                $"Le total du devis ne peut pas être inférieur au montant déjà encaissé ({AmountPaid:0.000} DT).");
        }

        var existingById = _installments.ToDictionary(i => i.Id);

        // Every installment carrying money must survive the revision — dropping one would erase collected
        // cash from the plan's balance with no trace.
        var keptIds = list.Where(i => i.id.HasValue).Select(i => i.id!.Value).ToHashSet();
        var droppedWithMoney = _installments.Where(i => i.AmountPaid > 0m && !keptIds.Contains(i.Id)).ToList();
        if (droppedWithMoney.Count > 0)
        {
            throw new InvalidOperationException(
                "Une échéance déjà encaissée ne peut pas être supprimée de l'échéancier. Conservez-la et ajustez les autres.");
        }

        var rebuilt = new List<Installment>();
        foreach (var (id, dueDate, amount) in list)
        {
            if (id.HasValue && existingById.TryGetValue(id.Value, out var existing))
            {
                existing.Revise(dueDate, amount); // guards amount >= AmountPaid
                rebuilt.Add(existing);
            }
            else
            {
                rebuilt.Add(new Installment(Guid.NewGuid(), Id, dueDate, amount));
            }
        }

        _installments.Clear();
        _installments.AddRange(rebuilt);
        Touch();
    }

    /// <summary>
    /// Reorder the plan's acts. <paramref name="itemIds"/> must be exactly this plan's acts, each once —
    /// a partial list would leave the rest at stale positions and silently interleave them. Cosmetic, so it
    /// does **not** bump the revision: nothing a patient signed for changes.
    /// </summary>
    public void SetItemOrder(IReadOnlyList<Guid> itemIds)
    {
        if (Status == TreatmentPlanStatus.Cancelled)
            throw new InvalidOperationException("Un plan annulé ne peut pas être réordonné.");

        if (itemIds.Count != _items.Count || itemIds.Distinct().Count() != itemIds.Count
            || itemIds.Any(id => _items.All(i => i.Id != id)))
        {
            throw new InvalidOperationException("La liste des actes ne correspond pas exactement à celle du devis.");
        }

        for (var position = 0; position < itemIds.Count; position++)
        {
            _items.First(i => i.Id == itemIds[position]).SetSequenceNumber(position);
        }
        Touch();
    }

    /// <summary>
    /// Stamp one completed amendment. Called **once** per user-visible change by the amend / revise-schedule
    /// handlers, deliberately not by <see cref="AddItems"/>, <see cref="RemoveItem"/> and
    /// <see cref="ReviseInstallments"/> themselves: a single amendment routinely composes several of them
    /// (adding an act *and* re-spreading the échéancier is one edit, not two), and self-bumping mutators made
    /// « révision 4 » out of two amendments — a number the patient's printout could never be matched against.
    /// </summary>
    public void RecordAmendment()
    {
        EnsureAmendable();
        RevisionNumber++;
        Touch();
    }

    private int NextSequenceNumber() => _items.Count == 0 ? 0 : _items.Max(i => i.SequenceNumber) + 1;

    /// <summary>An amendment only makes sense on a live devis: a Draft is edited outright, a Cancelled one is
    /// void, and a Completed one has no remaining treatment to change.</summary>
    /// <summary>
    /// The window in which a devis may still be corrected: <b>everything except a draft and a cancelled one</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It used to exclude <c>Completed</c>, and that was wrong on the owner's own terms</b>: a plan
    /// completes automatically the moment its last act is marked réalisé, so a fee typed wrong on a bridge
    /// became uncorrectable at the exact instant the work finished — the one moment a dentist is most likely to
    /// notice it. `EnsureCorrectable` already admitted Completed for the act-level corrections, so the two
    /// windows disagreed about the same plan.
    /// </para>
    /// <para>
    /// A <b>Draft</b> is refused because `SetItems` is its editor, and a <b>Cancelled</b> plan because it is a
    /// closed record kept for its number — correcting one would be rewriting history rather than fixing it.
    /// </para>
    /// </remarks>
    private void EnsureAmendable()
    {
        if (Status == TreatmentPlanStatus.Draft)
        {
            throw new InvalidOperationException("Un brouillon se modifie directement, pas par révision.");
        }
        if (Status == TreatmentPlanStatus.Cancelled)
        {
            throw new InvalidOperationException("Un devis annulé ne peut plus être modifié.");
        }
    }

    /// <summary>Cancel an accepted/in-progress plan (motif required). A draft is deleted, not cancelled.</summary>
    public void Cancel(string reason)
    {
        if (Status == TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Un brouillon se supprime, il ne s'annule pas.");
        if (Status == TreatmentPlanStatus.Cancelled)
            throw new InvalidOperationException("Le plan est déjà annulé.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation est requis.", nameof(reason));

        CancellationReason = reason.Trim();
        Status = TreatmentPlanStatus.Cancelled;
        Touch();
    }

    // Parked acts are excluded: they are no longer planned work, and leaving them in would keep a stopped
    // treatment claiming the money for the séances the patient is not coming back for.
    private void RecomputeTotal() =>
        TotalPlanned = InvoiceCalculator.RoundMoney(ActiveItems.Sum(i => i.PlannedCost));

    private void EnsureDraft()
    {
        if (Status != TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Seul un plan au statut brouillon peut être modifié.");
    }

    private void EnsureActive()
    {
        if (Status != TreatmentPlanStatus.Accepted && Status != TreatmentPlanStatus.InProgress)
            throw new InvalidOperationException("Le plan doit être accepté pour cette opération.");
    }

    /// <summary>
    /// A correction to what was already recorded may be applied to a <c>Completed</c> plan — unlike
    /// <see cref="EnsureActive"/>, which guards *doing* work. Marking the last act done closes the plan, so a
    /// correction gate that excluded <c>Completed</c> would lock out the exact mistake it needs to fix. A
    /// <c>Draft</c> has no realised act to undo and a <c>Cancelled</c> plan is void.
    /// </summary>
    private void EnsureCorrectable()
    {
        if (Status != TreatmentPlanStatus.Accepted
            && Status != TreatmentPlanStatus.InProgress
            && Status != TreatmentPlanStatus.Completed)
        {
            throw new InvalidOperationException("Seul un devis accepté, en cours ou terminé peut être corrigé.");
        }
    }

    /// <summary>
    /// Money may still be collected on a <c>Completed</c> plan. « Terminé » means every act was carried out,
    /// not that the patient has paid — treatment routinely finishes before the last échéance is collected, so
    /// closing the clinical track must never close the financial one. (Wider than <see cref="EnsureActive"/>,
    /// which still guards act completion.)
    /// </summary>
    private void EnsurePayable()
    {
        if (Status != TreatmentPlanStatus.Accepted
            && Status != TreatmentPlanStatus.InProgress
            && Status != TreatmentPlanStatus.Completed)
        {
            throw new InvalidOperationException("Le plan doit être accepté pour enregistrer un paiement.");
        }
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

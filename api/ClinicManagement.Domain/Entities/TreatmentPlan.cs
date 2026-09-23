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

    /// <summary>
    /// Why the balance was abandoned, and how much of it — written by <see cref="WriteOff"/>, null on every
    /// other plan.
    /// <para>
    /// ⚠️ <b>Its own field, never <see cref="CancellationReason"/>.</b> The two answer different questions
    /// about different outcomes: a cancellation says the document should never have existed, a write-off says
    /// the work was done and the money will not come. Sharing the column would make « pourquoi ce devis est-il
    /// clos ? » unanswerable from the row, and <see cref="Uncancel"/> already appends to the cancellation
    /// motif, so a shared field would grow a mixed history of two unrelated decisions.
    /// </para>
    /// </summary>
    public string? WriteOffReason { get; private set; }

    /// <summary>
    /// What was given up, captured <b>at the moment of the write-off</b> — <see cref="Outstanding"/> as it then
    /// stood. 0 on every plan that has not been written off.
    /// <para>
    /// ⚠️ <b>Stored rather than re-derived, because it cannot be re-derived afterwards.</b> Once the status is
    /// written the plan leaves every debt read, and `Outstanding` goes on moving if a late payment is voided or
    /// the acts are amended. « Combien le cabinet a-t-il passé en perte cette année ? » is a question a practice
    /// has, and only the writer knows the answer.
    /// </para>
    /// </summary>
    public decimal WriteOffAmount { get; private set; }

    /// <summary>
    /// What the patient owes for this devis (TND millimes) — the sum of the active acts' <b>net</b> costs,
    /// i.e. after any remise.
    /// <para>
    /// ⚠️ <b>Net, and that is deliberately where the remise lands.</b> Every money read in the product is built
    /// on this figure — the échéancier's <c>Σ Amount</c> invariant, « Créances », « Solde patient », la caisse,
    /// the dashboard and the note d'honoraires raised from the plan — so summing the net here is what makes a
    /// discount reach all of them at once, with nothing else having to learn the word « remise ».
    /// <see cref="TotalGross"/> and <see cref="TotalDiscount"/> keep the two halves readable for the devis
    /// itself and for reporting.
    /// </para>
    /// </summary>
    public decimal TotalPlanned { get; private set; }

    /// <summary>
    /// The acts' tarifs before any remise — what the devis prints on its lines. Derived, never stored: two
    /// stored totals are two things to keep in step, and this one has no invariant of its own.
    /// </summary>
    public decimal TotalGross =>
        InvoiceCalculator.RoundMoney(ActiveItems.Sum(i => i.PlannedCost));

    /// <summary>
    /// What the cabinet has given away on this devis. <c>TotalGross − TotalPlanned</c>, stated as its own
    /// figure because that is the number a practice reports.
    /// </summary>
    public decimal TotalDiscount =>
        InvoiceCalculator.RoundMoney(ActiveItems.Sum(i => i.DiscountAmount));

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
    /// <summary>
    /// May this plan be destroyed outright, rather than cancelled or stopped?
    ///
    /// <para>
    /// ⚠️ <b>Status is not enough any more, and asking only it made « Supprimer le brouillon » destructive.</b>
    /// Since « Suivre ce traitement » a <c>Draft</c> is a treatment under way — un-numbered, but carrying
    /// séances, recorded steps and links to the fiches that evidence them — and deleting one cascades:
    /// <c>TreatmentPlanConfiguration</c> and <c>TreatmentPlanItemConfiguration</c> both declare
    /// <c>DeleteBehavior.Cascade</c>, so the acts and their step rows go. The appointments' links are bare
    /// columns with NO foreign key — <c>DeleteTreatmentPlanCommand</c> releases them through
    /// <c>PlanBookingRelease</c> before the delete, or nothing would. The fiches survive attached to nothing — which is precisely the wreckage
    /// <see cref="StopTreatment"/> was written to avoid, and it names it as one of the three defects it fixed.
    /// </para>
    /// <para>
    /// <see cref="RemoveItem"/> has refused <c>HasDeliveredWork</c> per act all along; this is the same question
    /// asked for the whole plan, which is what was missing. A treatment that <i>has</i> been worked on is closed
    /// with <see cref="StopTreatment"/> (« Arrêter le traitement »), which keeps the séances and parks the rest.
    /// </para>
    /// </summary>
    public bool CanBeDeleted => Status == TreatmentPlanStatus.Draft && !AnyWorkRecorded;

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
    /// <remarks>
    /// ⚠️ <b>Gated by <see cref="EnsureAmendable"/>, because its own list contradicted it.</b> It admitted
    /// Draft / Accepted / InProgress while <c>EnsureAmendable</c> refuses only <c>Cancelled</c> — so on a
    /// <c>Stopped</c> or <c>Completed</c> devis, amending an act's <b>price</b> succeeded while correcting its
    /// <b>title</b> failed the whole save with « Seul un devis brouillon, accepté ou en cours peut être
    /// modifié. », to a dentist looking at a plan badged « Arrêté ». The amend modal always sends the title, so
    /// every amendment of a stopped plan hit it. One window, one owner.
    /// </remarks>
    public void UpdateDetails(string title, string? notes)
    {
        EnsureAmendable();
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

        /*
         * ⚠️ **A Draft that carries real work is no longer editable line by line, and this was silent data
         * loss.** `EnsureDraft` was the only gate, and since « Suivre ce traitement » a Draft is a *followed
         * treatment*: un-numbered, but carrying step protocols and links to the fiches that evidence the
         * séances already recorded. The rebuild below reuses each echoed-back **id** but constructs a brand-new
         * `TreatmentPlanItem` — empty `_steps`, `LinkedDentalRecordId = null`, `DoneDate = null`,
         * `Status = Planned`. So retyping a title from the plans list (« Modifier le brouillon », which opened
         * this form rather than the amend one) **deleted the protocol and every fiche link**, with no error.
         *
         * `CanBeDeleted` asks this exact question one method over, for the same reason — destroying an act
         * leaves the fiche attached to nothing, which is the wreckage `StopTreatment` was written to avoid.
         * The remedy named here is a real one: `AddItems`/`UpdateItems`/`RemoveItem` correct such a plan
         * in place, keeping every link.
         */
        if (_items.Any(i => i.HasSteps || i.HasDeliveredWork))
        {
            throw new InvalidOperationException(
                "Ce traitement a déjà des séances ou des étapes enregistrées : corrigez-le avec "
                + "« Modifier les actes et les prix », qui conserve les fiches de soins liées.");
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
    /// Record that a note d'honoraires already bills one of this plan's acts, holding that line at <b>0</b> —
    /// see <see cref="TreatmentPlanItem.BilledOnInvoiceId"/> for why the marker is stated rather than derived.
    /// <para>
    /// Called by <c>ContinueRecordedActCommand</c>'s billed path, <b>after</b> <see cref="SetItems(IEnumerable{TreatmentPlanItemInput}, bool)"/>
    /// and before the plan is accepted. Deliberately not a field on <see cref="TreatmentPlanItemInput"/>:
    /// <c>SetItems</c> rebuilds every act from its input, so a copy site that omitted the field would erase the
    /// marker and silently restore the double count — the shape that already cost this codebase an implant's
    /// osseointegration interval through <c>TreatmentPlanItemStepInput</c>'s fourth argument.
    /// </para>
    /// <para>
    /// Recomputes <see cref="TotalPlanned"/>, since holding a line at 0 changes it.
    /// </para>
    /// </summary>
    public void MarkItemBilledOnInvoice(Guid itemId, Guid invoiceId, decimal billedAmount)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.MarkBilledOnInvoice(invoiceId, billedAmount);
        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// « Détachez-la de ce traitement » — stop one note d'honoraires speaking for this plan's acts, without
    /// touching the note's own money, number or payments.
    ///
    /// <para>
    /// ⚠️ <b>This is the plan half of a remedy that three refusals name and nothing implemented.</b>
    /// <see cref="TreatmentPlanItem.Revise"/> refuses to re-price an act a note collects and tells the dentist
    /// to detach it; the only callers of the invoice half (<c>TreatmentPlanBridgeRelease</c>) were cancelling
    /// and stopping the treatment, so the only way to release a note was to kill the devis it was attached to.
    /// </para>
    /// <para>
    /// ⚠️ The freed acts stay at <b>0</b> — see <see cref="TreatmentPlanItem.ClearBilledOnInvoice"/> for why
    /// no price is invented — so <see cref="TotalPlanned"/> does not move here and the échéancier is untouched.
    /// Pricing them is the amendment that follows, with the dentist looking at the figures.
    /// </para>
    /// </summary>
    /// <returns>How many acts were released; 0 when this note carried none of them.</returns>
    public int DetachNote(Guid invoiceId)
    {
        EnsureAmendable();

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("La note d'honoraires est requise.", nameof(invoiceId));
        }

        var released = _items.Count(i => i.BilledOnInvoiceId == invoiceId && i.ClearBilledOnInvoice());
        if (released == 0)
        {
            return 0;
        }

        RecomputeTotal();
        RevisionNumber++;
        Touch();
        return released;
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

        /*
         * Ensure the accepted plan is payable: a devis with no échéancier gets a single lump-sum installment
         * for the full planned total, due at acceptance — otherwise Outstanding (derived from installments)
         * would be stuck at the total forever with no way to record a payment.
         *
         * ⚠️ `isAutoRaised: true` — and it is not a detail. This row is a **ledger container, not a promise**:
         * nobody agreed its date, it simply has to be dated something and « now » is the only instant
         * available. Left unmarked it made every devis in the database read « En retard » from the day after
         * signature (25 of 27 unpaid échéances, measured), which is how that badge came to mean nothing. See
         * `InstallmentLateness`, which is the only thing allowed to read the flag.
         */
        if (_installments.Count == 0 && TotalPlanned > 0m)
        {
            _installments.Add(new Installment(
                Guid.NewGuid(), Id, AcceptedDate.Value, TotalPlanned, isAutoRaised: true));
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
        ChequeDetails? cheque = null,
        Guid? dentalRecordId = null)
    {
        EnsurePayable();
        var installment = _installments.FirstOrDefault(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("Échéance introuvable.");

        var payment = installment.RecordPayment(amount, method, paidOn, cheque, dentalRecordId);
        if (Status == TreatmentPlanStatus.Accepted)
            Status = TreatmentPlanStatus.InProgress;
        Touch();
        return payment;
    }

    /// <summary>
    /// What one fiche de soins has already collected onto this treatment — the live rows only, so a voided
    /// payment stops counting exactly as it does everywhere else.
    ///
    /// <para>
    /// ⚠️ <b>This is what makes re-saving a fiche safe.</b> « Encaissé sur le traitement » carries the séance's
    /// <b>cumulative</b> figure, exactly as « Payé » does for a note d'honoraires, so the collector posts the
    /// difference rather than the amount — the shape <c>BillDentalRecordCommand.TopUpAsync</c> already uses.
    /// Without it, re-opening a fiche and pressing « Enregistrer » would take the money a second time, silently,
    /// on the most ordinary edit there is.
    /// </para>
    /// </summary>
    public decimal CollectedOnRecord(Guid dentalRecordId) =>
        InvoiceCalculator.RoundMoney(
            _installments
                .SelectMany(i => i.Payments)
                .Where(p => !p.IsVoided && p.DentalRecordId == dentalRecordId)
                .Sum(p => p.Amount));

    /// <summary>
    /// Take money at the chair for this treatment, spreading it over the échéancier from the earliest unpaid
    /// échéance onwards, and return the rows written.
    ///
    /// <para>
    /// ⚠️ <b>It spreads rather than requiring an échéance id, and that is the whole difference from
    /// <see cref="RecordInstallmentPayment"/>.</b> The dentist collecting 150 DT at the end of a séance is not
    /// looking at an échéancier and must not be asked which line to put it against — while a treatment quoted in
    /// three instalments has three lines, and a payment large enough to close the first legitimately runs into
    /// the second. <see cref="Installment.RecordPayment"/> refuses an over-payment per row, so posting the whole
    /// amount against the first line would fail on a schedule the patient is simply paying ahead of.
    /// </para>
    /// <para>
    /// ⚠️ The caller must bound the amount by <see cref="Outstanding"/> first: this throws once the schedule is
    /// full, which is correct as an invariant and useless as a message. <c>CollectOnTreatmentCommand</c> owns the
    /// French refusal.
    /// </para>
    /// </summary>
    /// <param name="dentalRecordId">
    /// The fiche this money was collected at, or <c>null</c> when it was not collected at a séance at all —
    /// « Régler le devis » at the desk (S3), which is the same spreading rule with no clinical record behind
    /// it. Nullable rather than a second method: the arithmetic, the over-payment refusal and the
    /// <c>Accepted → InProgress</c> promotion are identical, and a copy of them is what
    /// <c>RecordInstallmentPayment</c>'s per-row refusal already proves is easy to get wrong.
    /// <para>
    /// ⚠️ It is what makes a re-saved fiche idempotent (<see cref="CollectedOnRecord"/>), so a desk settlement
    /// passing <c>null</c> is deliberately <b>not</b> idempotent — pressing « Régler » twice takes the money
    /// twice, exactly as pressing « Encaisser » on an échéance twice does. The caller bounds it by
    /// <see cref="Outstanding"/>, which is what actually stops the second press.
    /// </para>
    /// </param>
    public IReadOnlyList<InstallmentPayment> CollectChairside(
        decimal amount,
        PaymentMethod method,
        DateTime paidOn,
        ChequeDetails? cheque,
        Guid? dentalRecordId)
    {
        EnsurePayable();

        var remaining = InvoiceCalculator.RoundMoney(amount);
        if (remaining <= 0m)
        {
            throw new ArgumentException("Le montant encaissé doit être supérieur à 0.", nameof(amount));
        }

        var written = new List<InstallmentPayment>();
        foreach (var installment in _installments.OrderBy(i => i.DueDate).ThenBy(i => i.Id))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var room = installment.Outstanding;
            if (room <= 0m)
            {
                continue;
            }

            var slice = Math.Min(room, remaining);
            written.Add(installment.RecordPayment(slice, method, paidOn, cheque, dentalRecordId));
            remaining = InvoiceCalculator.RoundMoney(remaining - slice);
        }

        if (remaining > 0m)
        {
            throw new InvalidOperationException(
                "Le montant encaissé dépasse ce qui reste dû sur ce traitement.");
        }

        if (Status == TreatmentPlanStatus.Accepted)
        {
            Status = TreatmentPlanStatus.InProgress;
        }
        Touch();
        return written;
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
    /// <summary>
    /// A fiche's date was corrected: the money collected at it and the séances it evidences move with it (G5),
    /// as the note d'honoraires' payments already did. Nothing else changes — no status, no total.
    /// </summary>
    public bool FollowDentalRecordDate(Guid dentalRecordId, DateTime newDate)
    {
        var moved = false;
        foreach (var installment in _installments)
        {
            foreach (var payment in installment.Payments
                         .Where(p => !p.IsVoided && p.DentalRecordId == dentalRecordId && p.PaidOn != newDate)
                         .ToList())
            {
                installment.AmendPaymentDate(payment.Id, newDate);
                moved = true;
            }
        }
        foreach (var item in _items)
        {
            moved |= item.RedateRecord(dentalRecordId, newDate);
        }
        if (moved) Touch();
        return moved;
    }

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
        AdvanceAfterWorkRecorded();

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
        //
        // ⚠️ Guarded, never assigned outright — see `StatusFollowsTheWork`. A correction to what was *recorded*
        // must not decide that a stopped treatment is running again, which is what stranded its parked acts.
        if (StatusFollowsTheWork)
        {
            Status = OpenStatusFromWork;
        }

        Touch();
    }

    /// <summary>
    /// Take back what this plan holds as evidence from one fiche de soins — every act and step it links to
    /// <paramref name="dentalRecordId"/>, except <paramref name="keepItemId"/>'s. For a fiche that was deleted,
    /// or re-saved no longer carrying the act.
    /// <para>
    /// On a live, stopped or completed plan this is the ordinary correction, with the status re-derived the way
    /// <see cref="UnmarkItemDone"/> does it. ⚠️ On a <b>cancelled or written-off</b> plan — void records kept for
    /// their number — only the pointers go and the status is left alone: the fiche they point at is gone or no
    /// longer says so, and refusing (as <see cref="EnsureCorrectable"/> does) made deleting such a fiche fail
    /// with a generic error, and « Aucun » on it impossible.
    /// </para>
    /// </summary>
    /// <returns>How many acts and steps were released.</returns>
    public int ReleaseDentalRecord(Guid dentalRecordId, Guid? keepItemId = null) =>
        Release(dentalRecordId, i => i.Id != keepItemId);

    /// <summary><see cref="ReleaseDentalRecord"/> for one act only.</summary>
    public int ReleaseDentalRecordFor(Guid itemId, Guid dentalRecordId) =>
        Release(dentalRecordId, i => i.Id == itemId);

    private int Release(Guid dentalRecordId, Func<TreatmentPlanItem, bool> which)
    {
        var released = 0;
        foreach (var item in _items.Where(which))
        {
            foreach (var step in item.Steps.Where(s => s.LinkedDentalRecordId == dentalRecordId).ToList())
            {
                if (item.UnmarkStep(step.Id))
                {
                    released++;
                }
            }
            // Read after the steps: a stepped act carries the record link only while its last step does.
            if (item.LinkedDentalRecordId == dentalRecordId && item.Unmark())
            {
                released++;
            }
        }

        if (released == 0)
        {
            return 0;
        }

        var isVoid = Status is TreatmentPlanStatus.Cancelled or TreatmentPlanStatus.WrittenOff;
        if (!isVoid && StatusFollowsTheWork)
        {
            Status = OpenStatusFromWork;
        }

        Touch();
        return released;
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
        AdvanceAfterWorkRecorded();

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

        // ⚠️ Guarded, for `UnmarkItemDone`'s reason — see `StatusFollowsTheWork`.
        if (StatusFollowsTheWork)
        {
            Status = OpenStatusFromWork;
        }

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

        /*
         * ⚠️ **A Draft may carry steps, and lifting that refusal is what makes a treatment cheap to start.**
         *
         * This used to throw « Le devis doit être accepté pour définir les étapes d'un acte. », and that one
         * line forced every road through a financial document: a séance is bookable only if the step row
         * exists, the step row needed an accepted plan, and `Accept` numbers the devis (gapless, cancellable
         * only, with a motif) *and* raises a lump-sum échéance for the whole total. So « cet acte prend
         * plusieurs séances » cost a numbered, accepted, money-bearing devis — measured on a real booking as
         * 2026-0023 at 800,000 DT, minted from the appointment dialog.
         *
         * A Draft is already safe to hold them: `Number` is nullable with the unique index filtered to
         * non-null (several drafts coexist), and `PlanBillingRules.CarriesDebt` excludes Draft from every
         * money read — so a draft treatment carries no claim *by construction*, not by convention.
         */

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.SetSteps(steps);

        // ⚠️ A Draft stays a Draft and a Stopped treatment stays stopped — see `StatusFollowsTheWork`, which
        // is the one place both exemptions live. Without the first, the recompute below would promote an
        // un-numbered treatment to `Accepted` merely for having been cut into séances, giving it a debt
        // `CarriesDebt` says is real with no number on it; without the second, saving a protocol on a stopped
        // devis would reopen it and strand the acts the stop parked.
        if (StatusFollowsTheWork)
        {
            // ⚠️ `StatusAfterWork`, not a fourth hand-written recompute — see that property. The inline copy
            // here reached `Accepted` directly, which is the shape that once turned a followed treatment into
            // an accepted devis with a null number and a live créance nobody had quoted.
            Status = StatusAfterWork;
        }

        Touch();
    }

    /// <summary>
    /// The status a plan lands on once its acts have spoken — <b>including</b> the « everything is done »
    /// answer, which <see cref="OpenStatusFromWork"/> deliberately does not give.
    /// <para>
    /// ⚠️ It exists because <see cref="SetItemSteps"/> wrote the whole expression out by hand, reaching
    /// <c>Accepted</c> without going through <see cref="OpenStatusFromWork"/>'s <see cref="Number"/> test — the
    /// exact shape whose other copy once promoted an un-numbered treatment into a debt-bearing status. Adding a
    /// step to a finished act reopens it and completing the last one closes it; both answers live here.
    /// </para>
    /// </summary>
    private TreatmentPlanStatus StatusAfterWork =>
        ActiveItems.Any() && ActiveItems.All(i => i.Status == TreatmentPlanItemStatus.Done)
            ? TreatmentPlanStatus.Completed
            : OpenStatusFromWork;

    /// <summary>
    /// Whether any clinical work is recorded against this plan — an act « réalisé » <b>or</b> one « en cours »
    /// because some of its steps are done. Reading only <c>Done</c> here would walk a plan back to
    /// « Accepté » while a bridge sat half-finished on it.
    /// </summary>
    private bool AnyWorkRecorded => _items.Any(i => i.HasDeliveredWork);

    /// <summary>
    /// What this plan's status is while it is open, derived from the work its acts carry — and, above all,
    /// <b>never a promotion of an un-numbered treatment</b>.
    ///
    /// <para>
    /// ⚠️ <b>Keyed on <see cref="Number"/>, not on the current <see cref="Status"/>, and that is the point.</b>
    /// The three callers reset the status from scratch, and one of them (<see cref="Reopen"/>) runs while the
    /// plan is <c>Completed</c>, so it cannot ask « was this a draft before? ». The number can: <see cref="Accept"/>
    /// is the only writer of it, so <b>no number means never quoted</b> — which is precisely the invariant at
    /// stake. <c>PlanBillingRules.CarriesDebt</c> reads <c>Accepted</c>/<c>InProgress</c> as real money owed, so
    /// an un-numbered plan wearing either would claim a total from a patient against a devis that does not exist.
    /// </para>
    ///
    /// <para>
    /// It exists because the expression was written out three times — in <see cref="UnmarkItemDone"/>,
    /// <see cref="UnmarkItemStep"/> and <see cref="Reopen"/> — while only <see cref="AdvanceAfterWorkRecorded"/>
    /// was given the draft guard when « Suivre ce traitement » made an un-numbered plan a live treatment. The
    /// <see cref="Reopen"/> copy was reachable in the product: <see cref="StopTreatment"/> admits a Draft and
    /// leaves it <c>Completed</c>, so « Arrêter le traitement » followed by « Reprendre le traitement » turned a
    /// followed treatment into an <c>Accepted</c> devis with a null number and a live créance for its full total.
    /// No error, and nothing on screen said so.
    /// </para>
    /// </summary>
    private TreatmentPlanStatus OpenStatusFromWork =>
        Number is null
            ? TreatmentPlanStatus.Draft
            : AnyWorkRecorded ? TreatmentPlanStatus.InProgress : TreatmentPlanStatus.Accepted;

    /// <summary>
    /// Whether this plan's status may be re-derived from the work its acts carry — asked by every correction
    /// path, and <b>false for a status a human chose</b>.
    ///
    /// <para>
    /// ⚠️ <b><c>Stopped</c> is here because re-deriving it loses data with no error.</b>
    /// « Arrêter le traitement » parks every act with no delivered work, and <see cref="Reopen"/> — reachable
    /// only from <c>Stopped</c> or <c>Completed</c> — is the <b>only</b> thing that brings them back. So
    /// « Détacher la fiche » on a stopped treatment wrote <c>InProgress</c>, which withdrew « Reprendre le
    /// traitement » from the header in the same breath and left the parked acts outside
    /// <see cref="ActiveItems"/>, outside <see cref="TotalPlanned"/> (already re-spread by the stop) and
    /// outside every count, with no route back. Nothing errored: the devis read as an ordinary live treatment
    /// that had silently shrunk.
    /// </para>
    /// <para>
    /// <c>Draft</c> is here for the reason <see cref="OpenStatusFromWork"/> keys on <see cref="Number"/>: an
    /// un-numbered treatment must never be promoted into a debt-bearing status by having had work recorded on
    /// it. Both are decisions somebody took; neither is something the acts may imply.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="Reopen"/> deliberately does <b>not</b> consult this — reopening is exactly the deliberate
    /// decision that lets the work speak again, and it restores the parked acts before it asks.
    /// </para>
    /// <para>
    /// ⚠️ <c>WrittenOff</c> and <c>Cancelled</c> are closed decisions too: editing a written-off act's séances
    /// used to write <c>InProgress</c>, bringing the debt back while <see cref="WriteOffAmount"/> still reported
    /// it as a loss. Only <see cref="Reopen"/> / <c>Uncancel</c> reopen them.
    /// </para>
    /// </summary>
    private bool StatusFollowsTheWork =>
        Status is not (TreatmentPlanStatus.Draft
            or TreatmentPlanStatus.Stopped
            or TreatmentPlanStatus.WrittenOff
            or TreatmentPlanStatus.Cancelled);

    /// <summary>
    /// The acts that still count as this plan's treatment — everything except the ones parked by
    /// <see cref="StopTreatment"/>. Every total, progress count and « is it finished » test reads this rather
    /// than <see cref="Items"/>, so a parked act contributes nothing while keeping its history.
    /// </summary>
    public IEnumerable<TreatmentPlanItem> ActiveItems => _items.Where(i => !i.IsWithdrawn);

    /// <summary>
    /// Would « Arrêter le traitement » have to <b>cancel</b> this devis rather than stop it?
    ///
    /// <para>
    /// True when a <b>numbered</b> devis has no delivered work at all: there is nothing to keep, so the plan
    /// cannot be closed on what was carried out — the number is spent and the document may be in the patient's
    /// hands, which is what a cancellation with a motif is for. <see cref="StopTreatment"/> refuses exactly this
    /// case and names the remedy in its message.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is a question, not a second copy of the rule.</b> The predicate is the one
    /// <see cref="StopTreatment"/> builds <c>kept</c> from, stated once so the caller can branch <i>before</i>
    /// the throw instead of parsing French prose out of it — the trap this repo names « never recover an
    /// outcome by matching French prose ». An un-numbered Draft answers false: it has no document, nothing to
    /// explain, and stopping is the ordinary outcome for a treatment that never started.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><see cref="AmountPaid"/> is part of the question, and leaving it out made the stop button
    /// destructive without saying so.</b> A devis can carry a deposit taken at acceptance with no séance yet
    /// delivered — the commonest shape there is — and with only the two tests below, « Arrêter le traitement »
    /// branched straight into <see cref="Cancel"/>, which drops the plan out of every caisse read
    /// (see <see cref="EnsureNoLiveMoney"/>). The dialog on that branch says « il n'y a donc rien à conserver »,
    /// which is exactly false when money was handed over. A devis holding money therefore takes the <i>stop</i>
    /// branch, where the balance is re-spread onto what was kept and nothing leaves the ledger.
    /// </remarks>
    public bool StopWouldCancel =>
        Number != null
        && !HasReceipts
        && !_items.Any(i => !i.IsWithdrawn && i.HasDeliveredWork);

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
        // A Draft closes too: « terminé » is a statement about the WORK, and an un-quoted treatment that is
        // finished is finished. It keeps its null `Number` — closing a treatment never mints a devis.
        // ⚠️ `TreatmentPlanLifecycle.IsLive`, never a hand-written triple — see `EnsureActive`.
        if (!TreatmentPlanLifecycle.IsLive(Status))
        {
            throw new InvalidOperationException("Ce traitement est déjà clôturé.");
        }
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
    public IReadOnlyList<TreatmentPlanItem> StopTreatment(DateTime dueDate, PaymentMethod? refundMethod = null)
    {
        // A Draft is stoppable too — « le patient ne revient plus » happens just as often before anyone
        // asked for a quote, and it must not be the one state with no way out.
        // ⚠️ `TreatmentPlanLifecycle.IsLive`, never a hand-written triple — see `EnsureActive`.
        if (!TreatmentPlanLifecycle.IsLive(Status))
        {
            throw new InvalidOperationException("Ce traitement est déjà clôturé.");
        }

        var parked = _items
            .Where(i => !i.IsWithdrawn && !i.HasDeliveredWork)
            .OrderBy(i => i.SequenceNumber)
            .ToList();
        var kept = _items.Where(i => !i.IsWithdrawn && i.HasDeliveredWork).ToList();

        /*
         * Nothing delivered. On a NUMBERED devis that is a cancellation — the number is spent and the document
         * may be in the patient's hands, so it needs a motif. On an un-numbered Draft there is no document and
         * nothing to explain: the treatment simply never started, and refusing here would leave the dentist on
         * a dead end for the most ordinary outcome of all.
         */
        if (kept.Count == 0 && Number != null)
        {
            /*
             * ⚠️ **Two refusals, because « annulez-le » stopped being reachable when money is involved.**
             * `StopWouldCancel` now answers false for a devis carrying a deposit (see its note), and
             * `Cancel` refuses one outright (`EnsureNoLiveMoney`) — so sending the dentist there would name a
             * remedy the product would then refuse, which is the defect shape this audit found four times.
             * With money on the devis the only honest remedy is the avoir, and it is named.
             */
            // G3: with a confirmed rendu the stop goes through — everything collected is given back today and
            // every act is parked (total 0), reversible by « Reprendre ».
            if (AmountPaid > 0m && refundMethod is null)
            {
                throw new InvalidOperationException(
                    $"Aucun acte de ce devis n'a été réalisé, mais {AmountPaid:0.000} DT y ont déjà été encaissés. "
                    + "Rendez-les au patient pour arrêter le traitement.");
            }

            if (AmountPaid <= 0m && !HasReceipts)
            {
                throw new InvalidOperationException(
                    "Aucun acte de ce devis n'a été réalisé : annulez-le (un motif est requis) plutôt que d'arrêter le traitement.");
            }
        }

        foreach (var item in parked)
        {
            item.Withdraw();
        }

        RecomputeTotal();

        // ⚠️ The « déjà encaissé » refusal moved INTO `RespreadSchedule`, which is the only writer that can
        // break the invariant and was the only one of four not asking. The remedy wording is this verb's.
        RespreadSchedule(dueDate, "avant d'arrêter le traitement", refundMethod);
        RevisionNumber++;

        /*
         * ⚠️ **This wrote `Completed` until 2026-09-09, and that is the defect this status exists for.** A
         * stopped treatment wore the badge « Terminé », so nothing in the database or on the screen could tell
         * « la patiente ne revient pas » from « le travail est fini » — and the workspace, testing « facturable »
         * before « terminé », then offered « Facturer » and hid « Reprendre le traitement » everywhere.
         *
         * `Stopped` is closed clinically (`EnsureActive` refuses it) and open financially
         * (`PlanBillingRules.CarriesDebt` includes it): the acts that were carried out are still owed.
         */
        Status = TreatmentPlanStatus.Stopped;
        Touch();
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
    /// <param name="dueDate">
    /// When the restored balance is due, from the clinic clock. ⚠️ Required since the échéancier is re-spread
    /// here — see the note below.
    /// </param>
    public void Reopen(DateTime dueDate)
    {
        /*
         * ⚠️ Both closed statuses, and `Completed` is the one that matters least. `Stopped` is what
         * « Arrêter le traitement » now writes; `Completed` stays accepted because a treatment closed
         * automatically on its last séance is routinely reopened by « Détacher la fiche », and because every
         * plan stopped before 2026-09-09 is sitting in `Completed` with parked acts to restore.
         */
        /*
         * ⚠️ `WrittenOff` is admitted here, and that is what keeps it from being a second absorbing state. A
         * write-off entered on the wrong devis, or a patient who turns up a year later with the money, both
         * need the same thing: the créance back. The operation is identical — restore the parked acts,
         * re-derive the open status, re-spread — so it is this verb rather than a near-duplicate of it.
         */
        if (Status != TreatmentPlanStatus.Completed
            && Status != TreatmentPlanStatus.Stopped
            && Status != TreatmentPlanStatus.WrittenOff)
        {
            throw new InvalidOperationException(
                "Seul un traitement terminé, arrêté ou passé en perte peut être repris.");
        }

        /*
         * ⚠️ The write-off record is CLEARED, not kept. Unlike a cancellation motif — which explains a
         * numbered document that may be in a patient's hands and stays true after `Uncancel` — `WriteOffAmount`
         * is a figure the practice reports as a loss. Leaving it behind on a plan that is collecting again
         * would double-count the year's pertes against a créance that came back.
         */
        WriteOffReason = null;
        WriteOffAmount = 0m;

        // `ToList()` first: `Restore` mutates, and counting a lazy sequence would restore only what is enumerated.
        var restored = _items.Select(i => i.Restore()).ToList().Count(r => r);
        Status = OpenStatusFromWork;
        if (restored > 0)
        {
            RecomputeTotal();
            /*
             * ⚠️ **The échéancier MUST be re-spread here, and leaving it behind put two different balances on
             * two screens.** The plan is debt-bearing the instant `Status` is written, and the two money reads
             * are computed differently: « Solde patient » is `TotalPlanned − AmountPaid` while « Créances », the
             * dashboard and `PatientDebtLines` sum `Amount − AmountPaid` over the installment ROWS. Restoring
             * the acts moves the first and not the second — a 1 200 DT devis stopped at 400 DT kept and 400 DT
             * collected reopened reading 800 DT owed on the patient's file and 0 DT in « Créances », with
             * `PatientDebtLines` then offering a payable room of 0 so the receptionist could not take the money.
             *
             * The comment that used to sit above this method said the échéancier is « deliberately not restored
             * — re-pricing them is the amendment that follows ». That is still true of the *prices*; it was
             * never an argument for leaving `Σ Amount != TotalPlanned` behind.
             */
            RespreadSchedule(dueDate, "avant de reprendre le traitement");
            RevisionNumber++;
        }
        Touch();
    }

    /// <summary>
    /// Bring the échéancier back into step with <see cref="TotalPlanned"/> after the total changed —
    /// <b>keeping the agreed dates</b>.
    /// <para>
    /// ⚠️ Public so a caller that changed the total <b>without</b> sending a schedule can be re-spread instead
    /// of refused (the booking dialog has no échéancier on screen to re-send).
    /// </para>
    /// </summary>
    /// <param name="refundMethod">
    /// When the new total is below what was collected: give the difference back today by this method (G3).
    /// Null keeps the refusal, which is what lets the screen ask first.
    /// </param>
    public void RespreadScheduleToTotal(DateTime dueDate, PaymentMethod? refundMethod = null)
    {
        // Re-read the total first: a caller that moved an act's remise directly (the duplicate) left it gross.
        RecomputeTotal();
        RespreadSchedule(dueDate, refundMethod: refundMethod);
    }

    /// <summary>Any live receipt on the devis, even one since given back — what `Cancel` refuses on.</summary>
    public bool HasReceipts => _installments.SelectMany(i => i.Payments).Any(p => !p.IsVoided && !p.IsRefund);

    /// <summary>What « Arrêter le traitement » would have to give back: collected beyond the work it keeps.</summary>
    public decimal RefundOnStop => TreatmentPlanLifecycle.IsLive(Status)
        ? Math.Max(0m, InvoiceCalculator.RoundMoney(
            AmountPaid - ActiveItems.Where(i => i.HasDeliveredWork).Sum(i => i.NetCost)))
        : 0m;

    /// <summary>What was collected beyond the current total — what a « rendu » would give back.</summary>
    public decimal ExcessCollected => Math.Max(0m, InvoiceCalculator.RoundMoney(AmountPaid - TotalPlanned));

    /// <summary>
    /// « Rendre au patient » (G3): the devis total fell below what was collected, and the dentist confirmed
    /// giving the difference back. Recorded as a negative ledger row dated <paramref name="refundedOn"/> (today),
    /// on the latest paid échéances first — so today's caisse shows the money leaving and no past day moves.
    /// No avoir: a devis is not a fiscal document.
    /// </summary>
    public decimal RefundExcess(PaymentMethod method, DateTime refundedOn)
    {
        var excess = ExcessCollected;
        if (excess <= 0m) return 0m;
        EnsurePayable();

        var remaining = excess;
        foreach (var row in _installments.Select((row, index) => (row, index))
                     .OrderByDescending(x => x.row.DueDate).ThenByDescending(x => x.index).Select(x => x.row)
                     .ToList())
        {
            if (remaining <= 0m) break;
            var take = Math.Min(row.AmountPaid, remaining);
            if (take <= 0m) continue;
            row.RecordRefund(take, method, refundedOn);
            remaining = InvoiceCalculator.RoundMoney(remaining - take);
        }
        Touch();
        return excess;
    }

    /// <summary>
    /// Keep <c>Σ Amount == TotalPlanned</c> — the invariant « Solde patient » and « Créances » agree on.
    /// <para>
    /// ⚠️ <b>It used to collapse the whole schedule into one lump sum due today</b>, so a remise, a parked act or
    /// « Total convenu » erased dates the patient had agreed to. Now (owner's decision, 2026-09-23): a lower
    /// total is taken off the <b>last</b> unpaid rows, a higher one is added to the last unpaid row; only when
    /// nothing is left unpaid does a new row appear, due <paramref name="dueDate"/>.
    /// </para>
    /// </summary>
    /// <param name="remedy">How the caller's verb is named in the refusal, so the sentence says what was done.</param>
    private void RespreadSchedule(
        DateTime dueDate, string remedy = "avant de réduire le total du devis", PaymentMethod? refundMethod = null)
    {
        // ⚠️ An un-numbered treatment has no échéancier: writing one here put « Solde à régler » on a plan
        // nobody quoted (G10). A void plan's rows are evidence and are not re-derived either (G11).
        if (Number is null && _installments.Count == 0) return;
        if (Status is TreatmentPlanStatus.WrittenOff or TreatmentPlanStatus.Cancelled) return;

        // ⚠️ `TotalPlanned` may never fall below what was collected — the ONE place that is enforced. At the
        // TOP, before any row moves. A confirmed rendu (G3) is the one way through.
        if (refundMethod is { } method && TotalPlanned < AmountPaid)
        {
            RefundExcess(method, dueDate);
        }
        EnsureTotalCoversCollected(remedy);

        var delta = InvoiceCalculator.RoundMoney(TotalPlanned - _installments.Sum(i => i.Amount));
        if (delta == 0m) return;

        // Latest agreed date last: that is where a change of total lands.
        var byDate = _installments.Select((row, index) => (row, index))
            .OrderBy(x => x.row.DueDate).ThenBy(x => x.index).Select(x => x.row).ToList();

        if (delta > 0m)
        {
            var lastUnpaid = byDate.LastOrDefault(i => i.Outstanding > 0m);
            if (lastUnpaid is not null)
            {
                lastUnpaid.Resize(lastUnpaid.Amount + delta);
            }
            else
            {
                // Same reason as `Accept`'s row: the date is required, not chosen.
                _installments.Add(new Installment(Guid.NewGuid(), Id, dueDate, delta, isAutoRaised: true));
            }
            return;
        }

        var toRemove = -delta;
        for (var k = byDate.Count - 1; k >= 0 && toRemove > 0m; k--)
        {
            var row = byDate[k];
            var room = InvoiceCalculator.RoundMoney(row.Amount - row.AmountPaid);
            if (room <= 0m) continue;

            var take = Math.Min(room, toRemove);
            toRemove = InvoiceCalculator.RoundMoney(toRemove - take);
            var left = InvoiceCalculator.RoundMoney(row.Amount - take);
            if (left == 0m && row.Payments.Count == 0)
            {
                _installments.Remove(row);
            }
            else
            {
                row.Resize(left);
            }
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
    /// <b><see cref="TreatmentPlanItem.HasDeliveredWork"/> is the only refusal.</b> An act nothing has been
    /// recorded against is a plan, and a plan is a thing people change their minds about — so a booked act is
    /// removable whether its rendez-vous is tomorrow or last week.
    /// </para>
    /// <para>
    /// ⚠️ It used to refuse an act the patient was still booked for, on the ground that removing it would leave
    /// an appointment row pointing at a vanished id with the patient still expected. That consequence is real
    /// and the remedy was wrong: the refusal made the dentist leave the devis, find the visit in the agenda,
    /// cancel it and come back, for the ordinary case of changing one's mind about work that has not started.
    /// The booking is the <i>caller's</i> to settle — <c>AmendTreatmentPlanCommandHandler</c> cancels a visit
    /// this act was the only reason for, and drops the act from one that carries others — which is the cascade
    /// this aggregate cannot perform and therefore cannot condition on.
    /// </para>
    /// </summary>
    public void RemoveItem(Guid itemId)
    {
        var item = EnsureItemRemovable(itemId);

        _items.Remove(item);
        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// Put <b>one</b> act aside without touching the rest of the treatment — « on ne fait finalement pas
    /// celui-là ». It leaves <see cref="TotalPlanned"/> and every progress count, and keeps its steps, their
    /// dates and their fiche links.
    ///
    /// <para>
    /// ⚠️ <b>This is the capability the whole complaint was asking for.</b> Parking was all-or-nothing in both
    /// directions: <see cref="StopTreatment"/> parks every undelivered act and <see cref="Reopen"/> restores
    /// every one, so « la patiente revient, mais seulement pour la couronne » meant reopening the entire
    /// treatment and re-inflating the total with the implants she had declined — straight back into
    /// « Créances ». And the only other way to drop an act, <see cref="RemoveItem"/>, is refused the moment
    /// anything was delivered, with a remedy three refusals deep (détacher la fiche → refused if the fiche is
    /// on a live note → whose own remedy is refused if the note holds a payment).
    /// </para>
    /// <para>
    /// ⚠️ <b>Unlike <see cref="RemoveItem"/> this accepts an act with delivered work</b>, and that is the
    /// difference that makes it useful: nothing is destroyed, so the séances already carried out keep their
    /// fiches and come back intact on <see cref="RestoreItem"/>. What it will not do is empty the plan — a
    /// treatment with no active act is a treatment that has been stopped, and <see cref="StopTreatment"/> is
    /// the verb that says so and writes the right status.
    /// </para>
    /// <para>
    /// ⚠️ The échéancier is re-spread, so <c>Σ Amount == TotalPlanned</c> survives — and
    /// <see cref="EnsureTotalCoversCollected"/> refuses to put an act aside if that would take the total below
    /// what the patient has already handed over.
    /// </para>
    /// </summary>
    public void WithdrawItem(Guid itemId, DateTime dueDate, PaymentMethod? refundMethod = null)
    {
        EnsureAmendable();

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        if (item.IsWithdrawn)
        {
            return;
        }

        if (ActiveItems.Count(i => i.Id != itemId) == 0)
        {
            throw new InvalidOperationException(
                "C'est le dernier acte actif de ce traitement : arrêtez le traitement plutôt que de le mettre de côté.");
        }

        item.Withdraw();
        RecomputeTotal();
        RespreadSchedule(dueDate, "avant de mettre cet acte de côté", refundMethod);
        RevisionNumber++;
        Touch();
    }

    /// <summary>
    /// Bring one parked act back, at whatever état its own steps derive — the mirror of
    /// <see cref="WithdrawItem"/>, and the per-act half of <see cref="Reopen"/>.
    /// <para>
    /// ⚠️ The plan's own status is <b>not</b> re-derived here, and that is deliberate: restoring one act of a
    /// <c>Stopped</c> treatment does not decide that the treatment is running again — <see cref="Reopen"/> is
    /// the verb for that, and <see cref="StatusFollowsTheWork"/> states in as many words that a status a human
    /// chose may not be overwritten by what the acts happen to say.
    /// </para>
    /// </summary>
    public void RestoreItem(Guid itemId, DateTime dueDate)
    {
        EnsureAmendable();

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        if (!item.Restore())
        {
            return;
        }

        RecomputeTotal();
        RespreadSchedule(dueDate, "avant de remettre cet acte au devis");
        RevisionNumber++;
        Touch();
    }

    /// <summary>
    /// Move this devis to the patient it was actually for.
    ///
    /// <para>
    /// ⚠️ <b>There was no way back from picking the wrong patient.</b> <see cref="PatientId"/> was
    /// constructor-only with no mutator, and the field is disabled in every edit mode — so a devis issued on the
    /// wrong file could only be deleted (Draft with no work) or retyped under a <b>new number</b>, spending a
    /// second one out of a per-clinic-per-year series to fix a typing mistake.
    /// </para>
    /// <para>
    /// ⚠️ Refused once <b>anything has been delivered</b>: a recorded séance belongs to the mouth it was carried
    /// out in, and moving the devis under it would re-file another patient's clinical history. The invoice half
    /// of the question — a devis already bridged to a note d'honoraires names a patient on a fiscal document —
    /// cannot be seen from here and is the handler's, exactly as the billed-amendment rule is.
    /// </para>
    /// </summary>
    public void ReassignPatient(Guid patientId)
    {
        EnsureAmendable();

        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Le patient est requis.", nameof(patientId));
        }
        if (patientId == PatientId)
        {
            return;
        }
        if (_items.Any(i => i.HasDeliveredWork))
        {
            throw new InvalidOperationException(
                "Des séances ont déjà été réalisées sur ce devis : il ne peut plus changer de patient. "
                + "Détachez-les de leurs fiches de soins avant de le déplacer.");
        }

        PatientId = patientId;
        Touch();
    }

    /// <summary>
    /// Refuse now what <see cref="RemoveItem"/> would refuse later, without removing anything — so a caller
    /// that has side effects to perform (cancelling the visit this act was the only reason for) can settle
    /// every refusal <b>before</b> the first irreversible one.
    /// <para>
    /// ⚠️ Splitting this out is the point: a rendez-vous cancelled for an act that then turns out to be
    /// un-removable is a phone call nobody can un-make.
    /// </para>
    /// </summary>
    /// <returns>The act, so the caller does not look it up twice.</returns>
    public TreatmentPlanItem EnsureItemRemovable(Guid itemId)
    {
        EnsureAmendable();

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        // One question — « has any of this happened? » — asked in two shapes, because a step-less act that is
        // Done has no séance count to quote and « a déjà 0 séance(s) réalisée(s) » would be the sentence.
        // ⚠️ An act part-way through is `InProgress`, not `Done`, so the first test alone let a bridge with two
        // of three séances delivered be deleted — with its step rows and the links to the two fiches that
        // evidenced them. Step-level removal has always refused this (« L'étape « X » est déjà réalisée et ne
        // peut pas être retirée »); the act level was the hole.
        if (item.Status == TreatmentPlanItemStatus.Done)
        {
            throw new InvalidOperationException(
                $"L'acte « {item.DesignationFr} » est déjà réalisé : détachez sa fiche de soins avant de le retirer du devis.");
        }
        if (item.HasDeliveredWork)
        {
            throw new InvalidOperationException(
                $"L'acte « {item.DesignationFr} » a déjà {item.StepsDone} séance(s) réalisée(s) et ne peut pas être retiré du devis. "
                + "Détachez ces séances de leur fiche de soins, ou arrêtez le traitement pour le mettre de côté sans perdre ce qui a été fait.");
        }

        return item;
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
        // Same rule as the respread branch, same owner — the échéancier may not be rewritten onto a total the
        // patient has already overpaid, whichever verb got here.
        EnsureTotalCoversCollected("avant de réviser l'échéancier");

        var existingById = _installments.ToDictionary(i => i.Id);

        // Every installment carrying money must survive the revision — dropping one would erase collected
        // cash from the plan's balance with no trace.
        var keptIds = list.Where(i => i.id.HasValue).Select(i => i.id!.Value).ToHashSet();
        var droppedWithMoney = _installments.Where(i => i.AmountPaid > 0m && !keptIds.Contains(i.Id)).ToList();
        // A row paid and then fully rendu (net 0, G3) is kept even when not echoed: it holds receipts dated on
        // past days, and dropping it would cascade them away.
        var historyOnly = _installments
            .Where(i => i.AmountPaid == 0m && i.Payments.Any(p => !p.IsVoided) && !keptIds.Contains(i.Id))
            .ToList();
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
                /*
                 * ⚠️ **An auto-raised row echoed back on its own day was agreed to by NOBODY, and promoting it
                 * put « En retard » back on devis nobody had scheduled.** `Revise` clears `IsAutoRaised` because
                 * revising is normally a dentist looking at a date and settling on it — true of « Modifier
                 * l'échéancier », false of the amend form, which round-trips the WHOLE échéancier on every save.
                 * So « ajouter un acte » or « corriger un prix » silently turned `Accept`'s ledger container into
                 * a promise dated the acceptance day: the workspace then printed that fabricated date instead of
                 * « Total dû — aucune échéance convenue », and `InstallmentLateness` took the typed branch and
                 * called it late from the next morning. Same defect as `RespreadSchedule`'s, one method over, and
                 * the same remedy — remember what the row WAS.
                 *
                 * Keyed on the DAY, not the instant: the form sends `dueDate.slice(0, 10) + "T00:00:00"`, so an
                 * untouched auto row comes back at midnight of the acceptance day rather than at the acceptance
                 * instant `Accept` wrote. A date the dentist actually MOVED lands on another day and promotes the
                 * row, which is the whole point of the échéancier.
                 */
                var untouchedAutoRow = existing.IsAutoRaised && existing.DueDate.Date == dueDate.Date;
                existing.Revise(dueDate, amount); // guards amount >= AmountPaid
                if (untouchedAutoRow) existing.MarkAutoRaised();
                rebuilt.Add(existing);
            }
            else
            {
                rebuilt.Add(new Installment(Guid.NewGuid(), Id, dueDate, amount));
            }
        }

        foreach (var row in historyOnly)
        {
            row.Resize(0m);
            rebuilt.Add(row);
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

        /*
         * ⚠️ **The set is the ACTIVE acts, not every row.** It demanded an exact match against `_items`, parked
         * acts included — so any surface reordering what it renders (which is `ActiveItems` everywhere: the
         * workspace table, its card twin, every count) was refused with a sentence naming nothing actionable.
         * A parked act has no position a human chose; it keeps its history and is appended after the live ones.
         */
        var active = ActiveItems.OrderBy(i => i.SequenceNumber).ToList();

        if (itemIds.Count != active.Count || itemIds.Distinct().Count() != itemIds.Count
            || itemIds.Any(id => active.All(i => i.Id != id)))
        {
            throw new InvalidOperationException(
                "La liste des actes ne correspond pas exactement aux actes actifs du devis.");
        }

        var position = 0;
        foreach (var id in itemIds)
        {
            _items.First(i => i.Id == id).SetSequenceNumber(position);
            position++;
        }

        // Parked acts keep their relative order, behind everything still being treated.
        foreach (var withdrawn in _items.Where(i => i.IsWithdrawn).OrderBy(i => i.SequenceNumber).ToList())
        {
            withdrawn.SetSequenceNumber(position);
            position++;
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
        /*
         * ⚠️ A **Draft is amendable now**, and the old refusal (« un brouillon se modifie directement, pas par
         * révision ») described a world where a Draft was a form somebody had not finished. It is a *followed
         * treatment* today — un-numbered, but carrying séances and recorded work — and it reaches the same
         * workspace as a numbered devis. Refusing here left that screen with no way to correct a total, which
         * is the one thing the dentist asked to be able to do at any moment.
         *
         * Nothing is bypassed: `RevisionNumber` on a Draft is meaningless but harmless (the devis prints no
         * revision mention at 0 and it is not yet printable at all), and the money invariants below still hold.
         */
        if (Status == TreatmentPlanStatus.Cancelled)
        {
            throw new InvalidOperationException("Un devis annulé ne peut plus être modifié.");
        }
        /*
         * ⚠️ A written-off devis is not amendable either, and the sentence names the way back. Amending
         * re-spreads the échéancier onto a new total — arithmetic about a balance the practice has already
         * declared it will not collect, and `WriteOffAmount` (the figure reported as a loss) would be left
         * describing a devis that no longer costs that.
         */
        if (Status == TreatmentPlanStatus.WrittenOff)
        {
            throw new InvalidOperationException(
                "La créance de ce devis est passée en perte : reprenez-le avant de le modifier.");
        }
    }

    /// <summary>
    /// Cancel an accepted/in-progress plan (motif required). A draft is deleted, not cancelled.
    /// <para>
    /// ⚠️ <b>Refused while the devis holds money</b> — see <see cref="EnsureNoLiveMoney"/>. Cancelling drops the
    /// plan out of every caisse read, so a cancellation over collected cash rewrites days that are already
    /// closed. This is the guard <c>Invoice.Cancel</c> has always had.
    /// </para>
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Un brouillon se supprime, il ne s'annule pas.");
        if (Status == TreatmentPlanStatus.Cancelled)
            throw new InvalidOperationException("Le plan est déjà annulé.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation est requis.", nameof(reason));

        EnsureNoLiveMoney("annulé");

        CancellationReason = reason.Trim();
        Status = TreatmentPlanStatus.Cancelled;
        Touch();
    }

    /// <summary>
    /// Give (or clear) the remise on one act of this devis.
    ///
    /// <para>
    /// Gated like a price change — <see cref="EnsureAmendable"/> — because it <b>is</b> one: it moves
    /// <see cref="TotalPlanned"/>, so the échéancier is re-spread in the same breath and
    /// <see cref="RevisionNumber"/> is bumped. A patient holding the earlier printout signed for a different
    /// total.
    /// </para>
    /// </summary>
    public void SetItemDiscount(Guid itemId, decimal discountAmount, DateTime dueDate, PaymentMethod? refundMethod = null)
    {
        EnsureAmendable();

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.SetDiscount(discountAmount);

        RecomputeTotal();
        // The remise lowers the total, so this is the writer that can break `TotalPlanned >= AmountPaid` —
        // `RespreadSchedule` is the one place that invariant is enforced, and it names this verb's remedy.
        RespreadSchedule(dueDate, "avant d'accorder cette remise", refundMethod);
        RevisionNumber++;
        Touch();
    }

    /// <summary>
    /// « Passer la créance en perte » — the practice abandons what is still owed and says so once.
    ///
    /// <para>
    /// ⚠️ <b>This is not <see cref="Cancel"/> and must never be done with it.</b> A cancellation states the
    /// document should not exist: it is refused outright once any money has been collected
    /// (<see cref="EnsureNoLiveMoney"/>) and it drops the whole devis out of la caisse, rewriting days that are
    /// already closed. A write-off states the opposite — the work WAS delivered and the payments WERE received
    /// — and abandons only the unpaid remainder. The collected rows, their receipts and their caisse days are
    /// untouched.
    /// </para>
    /// <para>
    /// ⚠️ <b>The échéancier is left exactly as it is</b>, for the same reason a voided payment keeps its row: it
    /// is the evidence of what was agreed and what was taken. What removes the balance from every money read is
    /// the status, through <c>PlanBillingRules.CarriesDebt</c> — one rule, already filtered on in SQL by all
    /// five debt reads, so nothing else has to learn about this status to stop counting it.
    /// </para>
    /// <para>
    /// Refused when there is nothing outstanding: a devis that is settled needs no forgiving, and writing off
    /// zero would put a plan into a closed, un-amendable state for no reason. Refused on a Draft (no debt was
    /// ever claimed — that is <c>CanBeDeleted</c>'s case) and on a Cancelled one (already void).
    /// </para>
    /// <para>
    /// Reversible through <see cref="Reopen"/>. A status nothing can leave is exactly the defect
    /// <see cref="Uncancel"/> had to be written for, and appending a second one would repeat it.
    /// </para>
    /// </summary>
    public void WriteOff(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif de la mise en perte est requis.", nameof(reason));
        }
        if (Status == TreatmentPlanStatus.Draft)
        {
            throw new InvalidOperationException(
                "Ce traitement n'a pas de devis : il ne réclame rien, il n'y a donc rien à passer en perte.");
        }
        if (Status == TreatmentPlanStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "Ce devis est annulé : il ne réclame plus rien. Rétablissez-le d'abord si la créance existe.");
        }
        if (Status == TreatmentPlanStatus.WrittenOff)
        {
            throw new InvalidOperationException("La créance de ce devis est déjà passée en perte.");
        }

        var abandoned = Outstanding;
        if (abandoned <= 0m)
        {
            throw new InvalidOperationException(
                $"Il ne reste rien à encaisser sur {Number ?? "ce devis"} : il n'y a pas de créance à passer en perte.");
        }

        WriteOffReason = reason.Trim();
        WriteOffAmount = abandoned;
        Status = TreatmentPlanStatus.WrittenOff;
        Touch();
    }

    /// <summary>
    /// Bring a cancelled devis back into service, with a motif of its own.
    ///
    /// <para>
    /// ⚠️ <b><c>Cancelled</c> was an absorbing state: nothing in the product could leave it.</b> Every guard
    /// excludes it — <see cref="EnsureAmendable"/>, <see cref="EnsureCorrectable"/>, <see cref="EnsurePayable"/>,
    /// <see cref="EnsureActive"/>, <see cref="SetItemSteps"/>, <see cref="SetItemOrder"/>,
    /// <see cref="VoidInstallmentPayment"/> — and <see cref="CanBeDeleted"/> is Draft-only, so it could not even
    /// be destroyed. A devis cancelled by mistake (and the stop button can now reach <see cref="Cancel"/>
    /// without the dentist choosing it) left the workspace with « Devis PDF » as its only surviving control.
    /// </para>
    /// <para>
    /// ⚠️ <b>The number is untouched and the original motif is kept.</b> <see cref="Number"/> was never released
    /// by the cancellation — the per-clinic-per-year series stays gapless whatever happens here — and the new
    /// motif is <b>appended</b> rather than written over, because the first one explains a document that may be
    /// in a patient's hands and un-cancelling does not make it untrue.
    /// </para>
    /// <para>
    /// The status is re-derived by <see cref="OpenStatusFromWork"/>, so an un-numbered plan comes back a Draft
    /// and a numbered one comes back on its own work — never promoted into a debt it did not carry before. The
    /// échéancier is re-spread for <see cref="Reopen"/>'s reason: the plan is debt-bearing again the instant the
    /// status is written, and <c>Σ Amount</c> must equal <see cref="TotalPlanned"/> by then.
    /// </para>
    /// </summary>
    public void Uncancel(string reason, DateTime dueDate)
    {
        if (Status != TreatmentPlanStatus.Cancelled)
        {
            throw new InvalidOperationException("Seul un devis annulé peut être rétabli.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif du rétablissement est requis.", nameof(reason));
        }

        CancellationReason = string.IsNullOrWhiteSpace(CancellationReason)
            ? $"Rétabli : {reason.Trim()}"
            : $"{CancellationReason} — rétabli : {reason.Trim()}";
        Status = OpenStatusFromWork;
        RecomputeTotal();
        RespreadSchedule(dueDate, "avant de rétablir ce devis");
        RevisionNumber++;
        Touch();
    }

    // Parked acts are excluded: they are no longer planned work, and leaving them in would keep a stopped
    // treatment claiming the money for the séances the patient is not coming back for.
    // ⚠️ `NetCost`, never `PlannedCost`: the remise is applied here and nowhere else, which is what makes it
    // reach the échéancier, every balance and the note d'honoraires without any of them knowing about it.
    private void RecomputeTotal() =>
        TotalPlanned = InvoiceCalculator.RoundMoney(ActiveItems.Sum(i => i.NetCost));

    private void EnsureDraft()
    {
        if (Status != TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Seul un plan au statut brouillon peut être modifié.");
    }

    /// <summary>
    /// The devis total may never be lower than what has already been collected against it — <b>one rule, one
    /// owner, consulted by every writer that can lower a total</b> (<see cref="RespreadSchedule"/>, and through
    /// it <see cref="StopTreatment"/>, <see cref="Reopen"/> and the amend handler's re-spread branch; plus
    /// <see cref="ReviseInstallments"/>, which rebuilds the rows itself).
    /// <para>
    /// ⚠️ It used to exist on two of those four. The gap was not theoretical: lowering a 500 DT devis with
    /// 500 DT collected to 300 DT clamps <see cref="Outstanding"/> at 0 and leaves 200 DT of the patient's
    /// money with nowhere to be — no credit line, no avoir prompt, and both balance reads showing 0.
    /// </para>
    /// </summary>
    /// <param name="remedy">What the caller was doing, so the sentence names it.</param>
    private void EnsureTotalCoversCollected(string remedy)
    {
        if (TotalPlanned >= AmountPaid)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{AmountPaid:0.000} DT ont déjà été encaissés sur ce devis, pour un total de {TotalPlanned:0.000} DT. "
            + $"Rendez la différence ({ExcessCollected:0.000} DT) au patient {remedy}.");
    }

    /// <summary>
    /// Refuse a destructive verb while the devis still holds money somebody handed over.
    /// <para>
    /// ⚠️ <b><see cref="Cancel"/> had no money test at all, and that is how a closed day lost cash.</b>
    /// <c>Cancelled</c> is absent from <c>PlanBillingRules.DebtBearingPlanStatuses</c> and all four caisse reads
    /// filter on it, so cancelling a devis that collected 500 DT in March **retroactively removes those 500 DT
    /// from March's extrait** — a day already closed and printed — from the dashboard, and from
    /// « Chèques à encaisser », while the fiche goes on printing « Encaissé sur le traitement 500,000 DT »
    /// because <c>GetCollectedByDentalRecordAsync</c> deliberately filters no status. Nothing errors anywhere.
    /// </para>
    /// <para>
    /// <c>Invoice.Cancel</c> has carried exactly this guard, with this reasoning, since it was written. The
    /// remedy is the same one <see cref="StopTreatment"/> names: an avoir, which leaves the ledger intact.
    /// </para>
    /// </summary>
    private void EnsureNoLiveMoney(string verb)
    {
        // Receipts, not the net: a payment later given back (G3) still sits on its own caisse day, and a
        // cancellation would drop it from there.
        var live = InvoiceCalculator.RoundMoney(
            _installments.SelectMany(i => i.Payments).Where(p => !p.IsVoided && !p.IsRefund).Sum(p => p.Amount));

        if (live <= 0m)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{live:0.000} DT ont déjà été encaissés sur ce devis : il ne peut plus être {verb}. "
            + "Arrêtez le traitement plutôt : ce qui a été encaissé reste sur le jour où il l'a été.");
    }

    /// <summary>
    /// The plan is open for clinical work — a séance may be booked against it and a fiche recorded.
    /// <para>
    /// ⚠️ <b>A Draft counts.</b> An un-numbered treatment is real work in progress; what it lacks is a
    /// <i>quote</i>, not a patient. Refusing it here is what forced « suivre ce traitement » to mint a
    /// numbered devis before the first séance could be recorded. Draft is still excluded from every money
    /// read by <c>PlanBillingRules.CarriesDebt</c>, so admitting it here adds clinical reach and no claim.
    /// </para>
    /// </summary>
    private void EnsureActive()
    {
        /*
         * ⚠️ **One statement of « which statuses are live », and this aggregate held FOUR copies of it.**
         * `TreatmentPlanLifecycle.LiveStatuses` is the owner — the « Traitements en cours » SQL filters on it,
         * `isPlanLive` mirrors it in the browser — while `UpdateDetails`, `Complete`, `StopTreatment` and this
         * method each wrote the triple out by hand. A sixth status appended to the enum would have moved the
         * repository and the recall worklist and left all four guards quietly admitting it.
         * `FollowedTreatmentLifecycleTests`' scan now covers `Domain/Entities` for exactly that.
         */
        if (!TreatmentPlanLifecycle.IsLive(Status))
        {
            throw new InvalidOperationException("Ce devis est clôturé : il ne peut plus recevoir de séance.");
        }
    }

    /// <summary>
    /// Advance the plan's own status after work landed on one of its acts — and <b>leave a Draft alone</b>.
    /// <para>
    /// A treatment with no number is not « accepté », « en cours » or « terminé »: those words describe a
    /// devis, and this one has not been quoted. Promoting it on the first recorded séance would give it a
    /// status `CarriesDebt` reads as real while `Number` is still null.
    /// </para>
    /// </summary>
    private void AdvanceAfterWorkRecorded()
    {
        if (Status == TreatmentPlanStatus.Draft)
        {
            return;
        }
        if (Status == TreatmentPlanStatus.Accepted)
        {
            Status = TreatmentPlanStatus.InProgress;
        }
        if (ActiveItems.Any() && ActiveItems.All(i => i.Status == TreatmentPlanItemStatus.Done))
        {
            Complete();
        }
    }

    /// <summary>
    /// A correction to what was already recorded may be applied to a <c>Completed</c> plan — unlike
    /// <see cref="EnsureActive"/>, which guards *doing* work. Marking the last act done closes the plan, so a
    /// correction gate that excluded <c>Completed</c> would lock out the exact mistake it needs to fix. Only a
    /// <c>Cancelled</c> plan is void.
    ///
    /// <para>
    /// ⚠️ <b>A <c>Draft</c> is correctable, and the note that said otherwise — « a Draft has no realised act to
    /// undo » — stopped being true the moment <see cref="EnsureActive"/> admitted one.</b> A followed treatment
    /// records séances while un-numbered, so it accumulates exactly the realised steps this gate exists to undo;
    /// excluding it meant a step attached to the wrong fiche could never be detached, and the refusal named
    /// « un devis accepté, en cours ou terminé » — three states the dentist had deliberately not put the
    /// treatment in, and could not reach without minting a devis nobody had asked for.
    /// </para>
    /// <para>
    /// Admitting it is only safe because <see cref="OpenStatusFromWork"/> keeps an un-numbered plan a Draft when
    /// the callers reset the status; the two changes belong together and must not be separated.
    /// </para>
    /// </summary>
    private void EnsureCorrectable()
    {
        // ⚠️ `Stopped` belongs here for the same reason `Completed` does: detaching a fiche recorded by mistake
        // is a correction, and refusing it on a stopped treatment would make the mistake permanent. Only a
        // cancelled devis — a closed record kept for its number — is beyond correction.
        if (Status != TreatmentPlanStatus.Draft
            && Status != TreatmentPlanStatus.Accepted
            && Status != TreatmentPlanStatus.InProgress
            && Status != TreatmentPlanStatus.Completed
            && Status != TreatmentPlanStatus.Stopped)
        {
            throw new InvalidOperationException("Ce devis est annulé : il ne peut plus être corrigé.");
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
        // ⚠️ `Stopped` is payable, and this is the whole point of the status. The patient owes the séances that
        // were carried out; refusing collection here would leave that money uncollectable on the one screen
        // that reports it, while « Créances » went on claiming it.
        if (Status != TreatmentPlanStatus.Accepted
            && Status != TreatmentPlanStatus.InProgress
            && Status != TreatmentPlanStatus.Completed
            && Status != TreatmentPlanStatus.Stopped)
        {
            throw new InvalidOperationException("Le plan doit être accepté pour enregistrer un paiement.");
        }
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

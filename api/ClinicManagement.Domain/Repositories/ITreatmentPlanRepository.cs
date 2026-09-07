using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One plan, reduced to what the « à rappeler » worklist needs to judge it: is it stalled, or unanswered?
///
/// <para>A projection, not a <see cref="TreatmentPlan"/>. The rules need five scalars and two item counts; loading
/// every plan in the clinic with its items, installments and payments to derive them is the over-fetch
/// <c>GetFilteredAsync</c> exists for and this read must avoid.</para>
/// </summary>
public sealed record RecallPlanFact(
    Guid PatientId,
    Guid PlanId,
    string? Number,
    TreatmentPlanStatus Status,
    DateTime CreatedAt,
    DateTime? AcceptedDate,
    int TotalItems,
    int DoneItems);

/// <summary>
/// One échéance-collection row behind the caisse statement — the plan side of <see cref="CaissePaymentRow"/>.
/// Keyed on the devis's own number, not an invoice's: an échéancier is collected against the devis.
/// </summary>
/// <summary>
/// What one fiche de soins collected onto a treatment, and which treatment that was — the read-back behind the
/// patient's fiche history.
/// </summary>
public sealed record DentalRecordCollectedRow(
    Guid DentalRecordId,
    Guid TreatmentPlanId,
    string? PlanNumber,
    decimal Amount);

/// <summary>
/// Which treatment a fiche de soins was a séance OF — the link, with no money in it.
///
/// <para>
/// ⚠️ <b>Separate from <see cref="DentalRecordCollectedRow"/> because they answer different questions, and
/// deriving one from the other is what left a whole class of séance unlabelled.</b> The money read can only see
/// a fiche that <i>collected</i> something, so a séance where the patient paid nothing that day — ordinary on a
/// six-visit implant — showed as « 0,000 DT · 0,000 DT » in the patient's history: indistinguishable from a free
/// ordinary visit, on a treatment with 1 500 DT outstanding. Belonging to a treatment is a clinical fact and
/// has to be read as one.
/// </para>
/// </summary>
public sealed record DentalRecordPlanLinkRow(
    Guid DentalRecordId,
    Guid TreatmentPlanId,
    string? PlanNumber,
    /// <summary>
    /// The act this fiche is a séance of — « Implant dentaire ». Carried so the history can say what the
    /// treatment is without the caller resolving the plan.
    /// </summary>
    string ActDesignationFr,
    /// <summary>
    /// The <b>step</b> this fiche carried out — « Pose de l'implant » — with its rank, or null for an act
    /// booked whole.
    ///
    /// <para>
    /// ⚠️ Without it a patient's history printed « Implant dentaire » three times over for one implant, with
    /// nothing saying the three rows were one treatment or which séance each was. The label and the rank exist
    /// on <c>TreatmentPlanItemStep</c> and were simply never read back.
    /// </para>
    /// </summary>
    string? StepLabel,
    int? StepNumber,
    int StepTotal);

public sealed record CaisseInstallmentPaymentRow(
    Guid PaymentId,
    Guid TreatmentPlanId,
    // ⚠️ Required, and it is what makes a banked mark writable at all: an `InstallmentPayment` sits two levels
    // inside the plan and is only addressable as {plan, installment, payment}, so a row that projected the plan
    // alone could be listed and never acted on.
    Guid InstallmentId,
    string? PlanNumber,
    Guid PatientId,
    decimal Amount,
    PaymentMethod Method,
    DateTime PaidOn,
    bool IsVoided,
    string? VoidReason,
    string? VoidedByName,
    // Cheque identity (L8) — see the sibling on `CaissePaymentRow`. Both ledgers carry it, because an échéancier
    // paid with a book of post-dated cheques is the archetypal case.
    string? ChequeNumber = null,
    string? ChequeBankName = null,
    DateTime? ChequeDueDate = null,
    // The banked mark (Group B) — see the sibling on `CaissePaymentRow`.
    DateTime? ChequeBankedOn = null,
    string? ChequeBankedByName = null);

public interface ITreatmentPlanRepository
{
    /// <summary>Load a treatment plan with its items and installments.</summary>
    Task<TreatmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every plan in the clinic holding an act linked to this fiche de soins, loaded with its items so the act can
    /// be un-marked. Backs the cleanup that runs when a fiche is deleted: <c>TreatmentPlanItem.LinkedDentalRecordId</c>
    /// is FK-less by design, so without this the act would stay « réalisé » pointing at a row that no longer exists —
    /// and, because marking an act done can auto-complete the plan, a deleted fiche could leave a devis closed
    /// against evidence that is gone. Normally returns zero or one plan.
    /// </summary>
    Task<IReadOnlyList<TreatmentPlan>> GetByLinkedDentalRecordAsync(
        Guid clinicId, Guid dentalRecordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every planned act of the clinic that is <b>under way</b> — some of its steps carried out and some still to
    /// come — as a flat projection, one row per act, paged.
    /// <para>
    /// This is the read behind « Traitements en cours ». It is a projection rather than the aggregates because a
    /// row of that list is a <i>(plan, act)</i> pair, so paging the plans would cut the list in the wrong place:
    /// a devis carrying three unfinished bridges is three rows, and a page of plans would return them as one.
    /// </para>
    /// <para>
    /// ⚠️ Ordered <b>most recent devis first</b> (<c>plan.CreatedAt</c> descending), then the act's own rank
    /// within its devis so a plan's acts stay in protocol order, with <c>ItemId</c> as the final, unique
    /// tie-break. Without that last one <c>OFFSET</c> over a non-unique sort shows one act on two pages and
    /// skips another, which reads as « un traitement a disparu ».
    /// <para>
    /// It used to be <i>oldest last séance first</i>, on the argument that the treatment nobody has come back
    /// for is the one the practice needs to see. That is still true, and it is now carried by the row itself
    /// rather than by the sort: « Dernière séance » turns amber past 14 days and « pas encore due » separates a
    /// protocol still waiting from one that has been forgotten. Newest-first is what the practice asked for,
    /// because the treatment just started is the one being talked about at the desk.
    /// </para>
    /// </para>
    /// <param name="searchTerm">
    /// Free text matched in SQL against the patient's name (both orders) and the devis number — never over the
    /// page in memory, which would answer a different question: this list is paged, so filtering the rows
    /// already fetched would search 25 acts and report « aucun résultat » for a patient on page 2.
    /// </param>
    /// <para>
    /// Filtered to plans that still carry work: <c>Accepted</c> and <c>InProgress</c>. A <c>Completed</c> plan has
    /// no act under way by definition, and a <c>Cancelled</c> one is void — listing either would put treatments
    /// nobody is expected to finish in front of the practice every morning.
    /// </para>
    /// </summary>
    Task<PagedResult<TreatmentInProgressFact>> GetTreatmentsInProgressAsync(
        Guid clinicId, PageRequest? paging, string? searchTerm = null, CancellationToken cancellationToken = default);

    /// <summary>List a clinic's treatment plans, filtered by patient / status / created-date range.</summary>
    /// <param name="acceptedFrom">
    /// Inclusive lower bound on <c>AcceptedDate</c> — a <b>different date</b> from <paramref name="from"/>, which
    /// bounds <c>CreatedAt</c>. Both exist because the dashboard's « Devis acceptés » KPI counts by the date the
    /// patient said yes, so drilling into it with the created-date range would show a different set of devis than
    /// the number counted. A plan with no <c>AcceptedDate</c> (still Draft) is excluded when this is supplied.
    /// </param>
    /// <param name="acceptedTo">Inclusive upper bound on <c>AcceptedDate</c>.</param>
    /// <param name="searchTerm">
    /// Matched in SQL over the devis number, title, notes and the patient's name (an EXISTS against
    /// <c>Patients</c> — names are resolved by a batched lookup after the page is cut, so the filter cannot live
    /// in the handler).
    /// </param>
    /// <param name="paging">The page to return, or null for every match (« Solde patient »).</param>
    Task<PagedResult<TreatmentPlan>> GetFilteredAsync(
        Guid clinicId,
        Guid? patientId = null,
        TreatmentPlanStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        DateTime? acceptedFrom = null,
        DateTime? acceptedTo = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the clinic's plans are in <paramref name="status"/>, optionally bounded by the date that status
    /// was reached. When <paramref name="byAcceptedDate"/> the bounds apply to <c>AcceptedDate</c> (« devis
    /// acceptés ce mois »); otherwise to <c>CreatedAt</c> (« devis en attente de réponse », a Draft has no
    /// accepted date). A count, not a list — the dashboard needs the number, never the aggregates.
    /// </summary>
    /// <summary>
    /// Plan facts for the whole clinic, for the « à rappeler » worklist. Excludes <c>Cancelled</c> plans (a void
    /// devis is nothing to chase) and <c>Completed</c> ones (nothing left to do). Item counts come from the
    /// database, never from a materialised collection.
    /// </summary>
    Task<IReadOnlyList<RecallPlanFact>> GetRecallPlanFactsAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    Task<int> CountByStatusAsync(
        Guid clinicId,
        TreatmentPlanStatus status,
        DateTime? from = null,
        DateTime? toInclusive = null,
        bool byAcceptedDate = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Devis a patient was shown and has not answered — a <c>Draft</c> with <b>no work recorded on it</b>.
    ///
    /// <para>
    /// ⚠️ <b>Its own read rather than <see cref="CountByStatusAsync"/> with <c>Draft</c>, because those two
    /// stopped meaning the same thing.</b> Since « Suivre ce traitement » an un-numbered plan is most often a
    /// treatment actively under way, so the dashboard's « Devis en attente de réponse » counted séances being
    /// carried out that week — and sent the practice chasing patients who have nothing to answer, since
    /// <c>Accept</c> is the only writer of <c>Number</c> and a Draft has therefore never been handed anything.
    /// The same premise was corrected in <c>RecallWorklistRules.IsUnanswered</c> and this copy was missed.
    /// </para>
    /// <para>
    /// « No work recorded » is <c>TreatmentPlanItem.HasDeliveredWork</c> stated in SQL — the act is
    /// <c>Done</c>, or one of its steps carries a <c>DoneDate</c>. Withdrawn acts are included: a treatment
    /// stopped mid-course is not a quote awaiting an answer either, and it cannot reach this count anyway
    /// (<c>StopTreatment</c> leaves the plan <c>Completed</c>).
    /// </para>
    /// </summary>
    Task<int> CountUnansweredDraftsAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest sequence number already assigned for a clinic in a given year (0 when none). The next
    /// accepted plan uses this + 1, giving a gapless per-clinic-per-year sequence (separate from invoices).
    /// </summary>
    Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Amount of installment payments collected in [from, to] across the clinic's <b>committed</b> plans
    /// (<c>PlanBillingRules.DebtBearingPlanStatuses</c> — a Draft devis's hand-built échéancier is not
    /// clinic money, and a cancelled plan is void). Approximated from the installment's cumulative
    /// <c>AmountPaid</c> and its last-payment date (<c>LastPaidOn</c>) — installments keep no per-payment
    /// history — so an installment topped up across two months is attributed to its last-payment month
    /// only. Feeds the dashboard "encaissé ce mois-ci" and the caisse.
    /// <para>
    /// Takes no billed-plan exclusion on purpose: this is <i>cash received</i>, not debt. The devis→facture
    /// bridge copies no payment onto the invoice, so suppressing a bridged plan here would delete real
    /// receipts from the caisse instead of de-duplicating them.
    /// </para>
    /// </summary>
    /// <param name="excludedPlanIds">
    /// Plans already represented by a non-cancelled bridge invoice. <b>Required</b>, exactly like the
    /// outstanding query's — so a cash read cannot silently skip the de-duplication.
    ///
    /// Since the devis→facture bridge now carries collected installment money onto the invoice at issue, a
    /// bridged plan's receipts live on the invoice track; counting them here as well would double the caisse.
    /// </param>
    Task<decimal> GetInstallmentCollectedBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Outstanding installment balance per patient (Σ amount − paid over not-fully-paid installments) plus
    /// the oldest overdue installment due date (an unpaid installment whose <c>DueDate</c> is before
    /// <paramref name="asOfUtc"/>), across the clinic's <b>committed</b> plans
    /// (<c>PlanBillingRules.DebtBearingPlanStatuses</c>) — only patients with a balance &gt; 0. Feeds the
    /// receivables list and aging, and the dashboard total-outstanding figure.
    /// </summary>
    /// <param name="excludedPlanIds">
    /// Plans already represented by an invoice, from <c>PlanBillingRules.BilledPlanIds</c> — their acts are
    /// counted through that invoice instead. Required (pass an empty set for none) so a new money read
    /// cannot silently omit the de-duplication and drift from « Solde patient ».
    /// </param>
    /// <summary>
    /// Every échéance collection recorded in <c>[from, toInclusive]</c>, for the « extrait de caisse ».
    /// <para>
    /// The row-level sibling of <see cref="GetInstallmentCollectedBetweenAsync"/> and it must stay
    /// predicate-for-predicate identical to it — including <paramref name="excludedPlanIds"/>. That parameter is
    /// not optional: a devis bridged into an issued invoice has its collections carried across onto the invoice
    /// track, so a statement that listed them here as well would show the same money twice *and* stop summing to
    /// the totals. Voided rows are returned (the caller strikes them through and excludes them from the balance).
    /// </para>
    /// </summary>
    /// <summary>
    /// How much each of <paramref name="dentalRecordIds"/> collected onto a treatment, summed over the
    /// <b>live</b> payments only — a voided one stops counting here exactly as it does everywhere else.
    ///
    /// <para>
    /// ⚠️ <b>Batched over the whole page, never one query per fiche.</b> This feeds the patient's fiche history,
    /// which lists every séance the patient has ever had; a per-row read there is the § 9.7 over-fetch that
    /// « Créances » was already corrected for.
    /// </para>
    /// <para>
    /// ⚠️ It is the only honest source for the figure. A séance of a multi-séance act is priced 0 on its fiche —
    /// the act is chiffré once, on the treatment — so the fiche's own <c>AmountPaid</c> is 0 and storing a copy
    /// of this figure beside it would be a second money field free to disagree with the ledger.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<DentalRecordCollectedRow>> GetCollectedByDentalRecordAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> dentalRecordIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which treatment each of these fiches was a séance of — see <see cref="DentalRecordPlanLinkRow"/>.
    ///
    /// <para>
    /// Reads BOTH link columns: <c>TreatmentPlanItemStep.LinkedDentalRecordId</c> (a multi-séance act, the
    /// common case) and <c>TreatmentPlanItem.LinkedDentalRecordId</c> (a single-séance act marked réalisé
    /// against its fiche). Asking only the step one would miss every plain act, and asking only the act one
    /// would miss every act still in progress — which is most of what this exists for.
    /// </para>
    /// <para>
    /// ⚠️ Batched over the page like its neighbour, and for the same § 9.7 reason.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<DentalRecordPlanLinkRow>> GetPlanLinksByDentalRecordAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> dentalRecordIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaisseInstallmentPaymentRow>> GetInstallmentPaymentsBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The plan half of la caisse's per-method breakdown — the same money
    /// <see cref="GetInstallmentCollectedBetweenAsync"/> sums, split by <c>PaymentMethod</c>.
    /// <para>
    /// ⚠️ Predicate-for-predicate identical to that SUM (committed plans only, bridged plans excluded, voided
    /// rows dropped, same inclusive bounds), for the same reason its invoice-side twin mirrors
    /// <c>GetCollectedByMethodBetweenAsync</c>: the breakdown is presented directly under <c>CashIn</c>, so
    /// <c>Σ breakdown == CashIn</c> has to be a property of the queries rather than a claim about them.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<PaymentMethodTotal>> GetInstallmentCollectedByMethodBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every **cheque** échéance collection of the clinic's committed plans, non-voided, optionally bounded by the
    /// cheque's own <c>ChequeDueDate</c> — the devis half of « chèques à encaisser ». First reader of
    /// <c>IX_InstallmentPayments_ChequeDueDate</c>.
    /// <para>
    /// ⚠️ <paramref name="excludedPlanIds"/> is <b>required</b> here too, and it is load-bearing rather than
    /// merely consistent: <c>IssueInvoiceCommand</c> carries a bridged plan's cheque onto the invoice payment
    /// (<c>InstallmentPayment.ToChequeDetails</c>), so without the exclusion one physical cheque would appear
    /// twice in the list of cheques to bank — and the two rows would be indistinguishable from two real cheques
    /// of the same amount.
    /// </para>
    /// <para>Rows with no due date are always returned, whatever the bounds — see the invoice-side twin.</para>
    /// </summary>
    Task<IReadOnlyList<CaisseInstallmentPaymentRow>> GetInstallmentChequePaymentsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> excludedPlanIds,
        DateTime? dueFrom = null,
        DateTime? dueTo = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestOverdueDueDate)>> GetInstallmentOutstandingByPatientAsync(
        Guid clinicId, DateTime asOfUtc, IReadOnlyCollection<Guid> excludedPlanIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which of <paramref name="itemIds"/> belong to a devis that actually carries patient debt — i.e. whose
    /// plan's status is in <c>PlanBillingRules.DebtBearingPlanStatuses</c>.
    ///
    /// <para>Behind « cette séance est couverte par un devis » in the closure worklist. It cannot be answered
    /// from the appointment alone: an appointment keeps its plan link after the devis is <b>cancelled</b>, and
    /// reading the link as proof of cover would leave those visits permanently reported as billed with nothing
    /// behind them — the exact « Facturé with no money » failure <c>AppointmentInvoiceLinks</c> excludes
    /// cancelled notes to avoid.</para>
    ///
    /// <para>A light id projection, bounded by the caller's set: the alternative is loading each plan with its
    /// acts and its échéancier to test one status.</para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetDebtBearingItemIdsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default);

    Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// One act under way, as the « Traitements en cours » list needs it: who, what, how far, and what is next.
/// <para>
/// A projection, not an entity graph — the list renders these fields and nothing else, and loading each plan with
/// its items, steps, installments and payments to print a patient's name and « 2 / 3 » would read most of the
/// clinic's devis history to fill one screen.
/// </para>
/// <para>
/// ⚠️ It deliberately does <b>not</b> say whether the next step is already booked. That is an appointment fact,
/// it needs the live-status rule <c>TreatmentPlanWorkflowProjection</c> owns, and expressing it here would be a
/// second copy of that rule in SQL — where no compiler checks it against the first. The reader answers it in one
/// batched appointment read over the page.
/// </para>
/// </summary>
public sealed record TreatmentInProgressFact(
    Guid PlanId,
    string? PlanNumber,
    Guid PatientId,
    Guid ItemId,
    string DesignationFr,
    int SequenceNumber,
    int StepsTotal,
    int StepsDone,
    Guid? NextStepId,
    string? NextStepLabel,
    int? NextStepSequenceNumber,
    int? NextStepEstimatedDurationMinutes,
    /// <summary>
    /// Calendar days the next step must wait after the previous one, from the protocol. Null where the protocol
    /// states no interval, which is « no opinion » — see <c>TreatmentPlanItemStep.MinDaysAfterPrevious</c>. The
    /// reader turns it and <see cref="LastStepDoneOn"/> into the due date the screen grades its alarm against;
    /// projecting the raw figure keeps the one addition out of SQL, where it is a date-arithmetic translation
    /// this query does not need.
    /// </summary>
    int? NextStepMinDaysAfterPrevious,
    /// <summary>When the most recent carried-out step happened — « dernière séance il y a 12 j ». Never null in
    /// practice (an act under way has at least one step done), but nullable so the shape cannot lie if it is.</summary>
    DateTime? LastStepDoneOn);

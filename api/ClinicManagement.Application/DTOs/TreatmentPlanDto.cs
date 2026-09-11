namespace ClinicManagement.Application.DTOs;

public class TreatmentPlanDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? Number { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public string? CancellationReason { get; set; }
    public decimal TotalPlanned { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Outstanding { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Post-acceptance amendments so far (0 = never amended). Printed as « · révision N » on the devis and
    /// the workspace header only when &gt; 0, so a patient holding an earlier printout can tell which version
    /// they signed. Persisted.
    /// </summary>
    public int RevisionNumber { get; set; }

    // ---- Derived (never persisted) -------------------------------------------------------------------
    // Clinical progress, always populated.
    public int ItemsDone { get; set; }
    public int ItemsTotal { get; set; }

    /// <summary>
    /// Earliest still-upcoming appointment across the plan's acts (« prochaine séance »), or null. Derived,
    /// so a cancelled appointment stops counting immediately. Populated on the query paths only.
    /// </summary>
    public DateTime? NextAppointmentAt { get; set; }

    /// <summary>
    /// The non-cancelled invoice this devis was billed into, when one exists — the plan is then represented by
    /// that invoice in « Solde patient ». Populated on the query paths only.
    /// </summary>
    public Guid? LinkedInvoiceId { get; set; }
    public string? LinkedInvoiceNumber { get; set; }
    public string? LinkedInvoiceStatus { get; set; }

    /// <summary>
    /// What the linked note is worth, and what is still owed <b>on it</b> — the figures that replace this plan's
    /// own <see cref="Outstanding"/> everywhere a balance is printed for a bridged devis.
    /// <para>
    /// ⚠️ Once a note holds the money, <see cref="Outstanding"/> is <c>TotalPlanned − Σ this plan's own
    /// installments</c> over an auto-raised échéance that will never see a payment, so it reports the whole devis
    /// as unpaid about a patient who owes nothing — measured on 4 of 4 bridged plans, including two fully
    /// settled, one of them shown in red with an « En retard » badge. Null when no note bills this devis, which
    /// is what makes « use the plan's own figure » the safe default.
    /// </para>
    /// </summary>
    public decimal? LinkedInvoiceTotal { get; set; }
    public decimal? LinkedInvoiceOutstanding { get; set; }

    /// <summary>
    /// The notes d'honoraires that already collect an act this devis holds at <b>0</b> — empty for every
    /// ordinary plan.
    ///
    /// <para>⚠️ <b>The opposite arrangement from <see cref="LinkedInvoiceId"/> and never to be confused with
    /// it.</b> A <i>linked</i> note <b>represents</b> the devis: every money read drops the plan and « Encaisser »
    /// disappears from its échéancier. A <i>carried</i> note collects one act while the devis stays live and
    /// collectable — the two documents are deliberately disjoint and <b>both</b> are read. Populated on the query
    /// paths and on <c>ContinueRecordedActCommand</c>'s own response.</para>
    /// </summary>
    public List<PlanCarriedInvoiceDto> CarriedInvoices { get; set; } = new();

    /// <summary>
    /// What the whole treatment is worth across every document it touches — this devis plus each carried note —
    /// or <b>null</b> when nothing is carried, which is every ordinary plan.
    ///
    /// <para>⚠️ <b>Served, never derived in the browser</b>, on <c>displayedOutstanding</c>'s precedent: the
    /// client cannot apply <c>PlanBillingRules</c>, and the one time it tried, 4 of 4 bridged plans were reported
    /// as wholly unpaid. Null means « nothing to state » and the money card keeps the shape it has always had,
    /// so no existing plan renders differently.</para>
    ///
    /// <para>⚠️ The three are <b>all</b> taken from the notes' own totals (not from
    /// <see cref="TreatmentPlanItemDto.BilledOnInvoiceAmount"/>), so <c>total − collected == outstanding</c>
    /// holds by construction. Where a note bills more than this treatment,
    /// <see cref="PlanCarriedInvoiceDto.BillsOtherWork"/> says so rather than the figure quietly overstating it.</para>
    /// </summary>
    public decimal? TreatmentTotal { get; set; }

    /// <inheritdoc cref="TreatmentTotal"/>
    public decimal? TreatmentCollected { get; set; }

    /// <inheritdoc cref="TreatmentTotal"/>
    public decimal? TreatmentOutstanding { get; set; }

    public List<TreatmentPlanItemDto> Items { get; set; } = new();
    public List<InstallmentDto> Installments { get; set; } = new();
}

/// <summary>
/// A note d'honoraires collecting an act that this — live, un-bridged — devis carries at 0.
/// </summary>
public class PlanCarriedInvoiceDto
{
    public Guid InvoiceId { get; set; }
    public string? Number { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>The note's own TTC, collected and outstanding — what the patient settles on that document.</summary>
    public decimal Total { get; set; }

    /// <inheritdoc cref="Total"/>
    public decimal Collected { get; set; }

    /// <inheritdoc cref="Total"/>
    public decimal Outstanding { get; set; }

    /// <summary>
    /// The share of that note which is this treatment's act(s) — the sum of every carried
    /// <see cref="TreatmentPlanItemDto.BilledOnInvoiceAmount"/> pointing at it.
    /// </summary>
    public decimal BilledActAmount { get; set; }

    /// <summary>
    /// True when the note bills more than this treatment's acts — a détartrage done in the same séance, say.
    /// The screens state it rather than letting « le traitement » quietly include somebody else's filling.
    /// </summary>
    public bool BillsOtherWork { get; set; }
}

public class TreatmentPlanItemDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// The clinic's own procedure this act is performed as, when the line was chosen from that menu. Lets
    /// booking the act preselect the procedure (colour + default duration on the appointment, and the act
    /// proposal in the dental-record modal). Null on CNAM-only, hand-typed, and pre-migration lines.
    /// </summary>
    public Guid? ProcedureTypeId { get; set; }

    public string DesignationFr { get; set; } = string.Empty;
    public List<int> ToothNumbers { get; set; } = new();
    public decimal PlannedCost { get; set; }

    /// <summary>
    /// The note d'honoraires that already collects this act's fee, when the devis holds it at <b>0</b> — null on
    /// every ordinary line. See <c>TreatmentPlanItem.BilledOnInvoiceId</c>.
    ///
    /// <para>⚠️ It is what lets the act row <b>withhold the price field</b> and name the note instead. A bare
    /// « 0,000 DT » on a line whose fee a patient has already part-paid is the exact complaint that produced
    /// this — and the rule was already written one file over, in <c>act-card.tsx</c>'s « Aucun honoraire sur
    /// cette séance ».</para>
    /// </summary>
    public Guid? BilledOnInvoiceId { get; set; }

    /// <inheritdoc cref="BilledOnInvoiceId"/>
    public string? BilledOnInvoiceNumber { get; set; }

    /// <summary>What that note bills for this act — the figure the row states in place of the 0.</summary>
    public decimal BilledOnInvoiceAmount { get; set; }

    /// <summary>What is still owed on that note, so the row can send whoever is reading it to the right door.</summary>
    public decimal BilledOnInvoiceOutstanding { get; set; }

    /// <summary>
    /// The teeth this act has actually been carried out on, unioned over the fiches its séances produced —
    /// <b>derived, never stored</b>, and distinct from <c>ToothNumbers</c>, which is what the devis LINE says.
    ///
    /// <para>
    /// ⚠️ <b>It exists because a devis line is very often an « acte général » with no teeth at all.</b> The
    /// fiche's chart selection is prefilled from the line, so a dentist who marked tooth 38 on séance 1 was
    /// offered a blank chart on séance 2 — and an implant's three fiches recorded the teeth once between them,
    /// on whichever séance the dentist happened to fill them in.
    /// </para>
    /// <para>
    /// ⚠️ It became <b>load-bearing rather than convenient</b> the moment the odontogram stopped being charted
    /// from the first séance (<c>ToothChartingRules</c>): the chart is written when the act finishes, so teeth
    /// entered early and absent from the last fiche would chart nothing at all.
    /// </para>
    /// </summary>
    public List<int> TreatedToothNumbers { get; set; } = new();

    public string Status { get; set; } = string.Empty;
    public DateTime? DoneDate { get; set; }
    public Guid? LinkedDentalRecordId { get; set; }

    /// <summary>Clinical order within the plan (0-based). Persisted; acts are returned already sorted.</summary>
    public int SequenceNumber { get; set; }

    // ---- Derived (never persisted) -------------------------------------------------------------------
    /// <summary>
    /// The appointment that currently speaks for this act — the earliest upcoming live one, else the most
    /// recent past live one. Null when nothing is booked, including when the only linked appointment was
    /// cancelled or a no-show (so the act returns to « À planifier » and can be booked again).
    /// Populated on the query paths only.
    /// </summary>
    public Guid? ScheduledAppointmentId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ScheduledAppointmentStatus { get; set; }

    /// <summary>
    /// The act's clinical steps in order — « Préparation, Empreinte, Scellement ». <b>Empty for an act done in
    /// one séance</b>, which is every line written before steps existed and most written after, so a client that
    /// ignores this field behaves exactly as it did.
    /// </summary>
    public List<TreatmentPlanItemStepDto> Steps { get; set; } = new();

    /// <summary>How many of <see cref="Steps"/> are carried out. Derived from the rows, always populated.</summary>
    public int StepsDone { get; set; }

    /// <summary>
    /// The next step still to carry out, or null when there is none (or the act has no steps at all) — what the
    /// row's single primary action names: « Planifier le scellement ».
    /// </summary>
    public Guid? NextStepId { get; set; }

    /// <summary>
    /// The earliest date the next step should be carried out, from the interval it carries and the date of the
    /// step before it — null when the act has no next step, states no interval, or has nothing delivered to
    /// count from (the ordinary case, and « no opinion »).
    /// <para>
    /// This is what lets a screen distinguish « pas encore due » from « oubliée ». Without it the worklist had
    /// only a flat fortnight to alarm on, so an implant waiting the eight to twelve weeks its own protocol
    /// specifies read exactly like a treatment the practice had forgotten.
    /// </para>
    /// </summary>
    public DateTime? NextStepDueFrom { get; set; }

    /// <summary>
    /// Parked by « Arrêter le traitement »: no longer part of the treatment, contributing to no total and no
    /// progress count, and keeping every step, date and fiche link it had. Restored by « Reprendre ».
    /// </summary>
    public bool IsWithdrawn { get; set; }
}

/// <summary>One clinical step of a planned act. Carries no money — the fee lives once on the act.</summary>
public class TreatmentPlanItemStepDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>Clinical order within the act (0-based, dense). Steps are returned already sorted.</summary>
    public int SequenceNumber { get; set; }

    public DateTime? DoneDate { get; set; }

    /// <summary>The fiche de soins that evidences this step. <b>Per step</b> — which is what lets one devis act
    /// be recorded across several fiches.</summary>
    public Guid? LinkedDentalRecordId { get; set; }

    public int? EstimatedDurationMinutes { get; set; }

    /// <summary>
    /// Calendar days to wait after the previous séance, when the protocol states one — chair time and waiting
    /// time are two different quantities; see <c>TreatmentPlanItemStep.MinDaysAfterPrevious</c>.
    /// </summary>
    public int? MinDaysAfterPrevious { get; set; }

    // ---- Derived (never persisted) -------------------------------------------------------------------
    /// <summary>
    /// The appointment that currently speaks for <b>this step</b>, by the same rule the act uses. Null when the
    /// step is not booked — including when its only linked visit was cancelled, so it becomes bookable again.
    /// <para>
    /// Separate from the act's own <c>ScheduledAppointmentId</c> on purpose: an act with one of three séances
    /// booked is « planifié » as an act and has two unbooked steps, and one field cannot say both.
    /// </para>
    /// </summary>
    public Guid? ScheduledAppointmentId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ScheduledAppointmentStatus { get; set; }
}

public class InstallmentDto
{
    public Guid Id { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Derived from the payment ledger. No longer monotonic — a payment can be voided.</summary>
    public decimal AmountPaid { get; set; }

    public decimal Outstanding { get; set; }
    public bool IsPaid { get; set; }

    /// <summary>
    /// Whether this échéance is genuinely late — <b>computed server-side by
    /// <c>InstallmentLateness.IsLate</c></b> and never re-derived on the client.
    ///
    /// <para>
    /// ⚠️ <b>It has to be served, because the client cannot answer it.</b> The rule needs the plan's status,
    /// whether a note d'honoraires already represents it, whether any act is still unrealised, the clinic's own
    /// calendar day, and whether the row's date was ever agreed — and the two surfaces that used to answer it
    /// locally (the échéancier table and its card form) each wrote <c>isBeforeToday(dueDate)</c>, which is a
    /// different, simpler question with a different answer on <b>25 of 27</b> unpaid rows in the dev database.
    /// </para>
    /// </summary>
    public bool IsOverdue { get; set; }

    /// <summary>
    /// True when nobody agreed this date — see <c>Installment.IsAutoRaised</c>. Served so the échéancier can
    /// say « échéance non convenue » instead of printing a date the patient never saw as though it were a
    /// commitment, and so « Modifier l'échéancier » can be offered as the way to make it one.
    /// </summary>
    public bool IsAutoRaised { get; set; }

    /// <summary>Derived: the most recent LIVE payment's method/date.</summary>
    public string? LastMethod { get; set; }
    public DateTime? LastPaidOn { get; set; }

    /// <summary>Every payment received against this échéance, each on its own date. Oldest first.</summary>
    public List<InstallmentPaymentDto> Payments { get; set; } = new();
}

/// <summary>One payment received against an échéance. Voidable; a voided row is kept and marked.</summary>
public class InstallmentPaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedByName { get; set; }

    /// <summary>
    /// The fiche de soins this money was handed over at, when it was collected chairside — null for the till
    /// and échéancier routes, and for every row written before the column existed.
    ///
    /// <para>
    /// ⚠️ It is what lets the échéancier say <b>which séance</b> a payment came from. Since a treatment is
    /// collected a visit at a time, one lump-sum échéance now routinely holds several payments, and a column of
    /// identical « 500,000 DT » lines that cannot be told apart is the state a dentist has to reconcile against
    /// the patient's fiche history by hand.
    /// </para>
    /// </summary>
    public Guid? DentalRecordId { get; set; }
}

/// <summary>One requested act line when creating/updating a treatment plan (catalog act or free-text).</summary>
public class TreatmentPlanItemRequest
{
    /// <summary>
    /// The existing act this line stands for, echoed back by the client. When it matches a line already on
    /// the plan, that line keeps its id — so an appointment or dental-record link to the act survives the
    /// edit. Unknown ids are treated as a new line, never an error (a stale client must not fail the save).
    /// </summary>
    public Guid? Id { get; set; }


    /// <summary>
    /// The clinic's own procedure this act will be performed as, when the caller picked one. Persisted so
    /// booking the act later can preselect it. The only catalog a devis line comes from — a procedure is a
    /// service you schedule and sell, a DCH code is the regulatory code for one clinical situation, and several
    /// codes can bill as the same procedure. An unknown or cross-clinic id is stored as sent and simply fails
    /// to resolve at booking time; it is never trusted for pricing.
    /// </summary>
    public Guid? ProcedureTypeId { get; set; }

    public string DesignationFr { get; set; } = string.Empty;
    public decimal PlannedCost { get; set; }
    public List<int> ToothNumbers { get; set; } = new();

    /// <summary>
    /// The séances this act will be carried out over, <b>as the dentist confirmed them</b> — the procedure's
    /// protocol with whatever they unticked or edited before accepting the devis.
    /// <para>
    /// ⚠️ <b>Tri-state, and the three states are all real.</b> <c>null</c> means « the client did not decide »
    /// and the act takes its procedure's catalogue protocol (an older client, an import, the
    /// <c>InitializeDefaultProcedureTypes</c> path); an <b>empty list</b> means « this act is one séance » and
    /// is an explicit refusal of that protocol; a non-empty list is the confirmed sequence. Reading an empty
    /// list as « not supplied » would make unticking every step impossible, which is exactly the flexibility
    /// the feature is for.
    /// </para>
    /// </summary>
    public List<TreatmentPlanItemStepRequest>? Steps { get; set; }
}

/// <summary>One séance of an act, as sent when a devis is created or amended.</summary>
public class TreatmentPlanItemStepRequest
{
    /// <summary>
    /// The existing step this line stands for, echoed back so it keeps its id — and with it its
    /// <c>DoneDate</c>, its fiche link and any appointment row pointing at it. Null means a new step.
    /// <para>
    /// ⚠️ Required for an amendment to be able to <b>edit</b> steps rather than replace them: without it every
    /// save of a stepped act would be a delete-and-recreate, which the aggregate refuses outright as soon as
    /// one step is carried out.
    /// </para>
    /// </summary>
    public Guid? Id { get; set; }

    public string Label { get; set; } = string.Empty;
    /// <summary>Chair time, when the protocol estimates one. Null is « unknown », never « zero minutes ».</summary>
    public int? EstimatedDurationMinutes { get; set; }

    /// <summary>
    /// Calendar days to wait after the previous séance, when the protocol states one — a different quantity
    /// from the chair time above; see <c>TreatmentPlanItemStep.MinDaysAfterPrevious</c>.
    /// </summary>
    public int? MinDaysAfterPrevious { get; set; }
}

/// <summary>One requested installment (échéance) when setting a plan's payment schedule.</summary>
public class InstallmentRequest
{
    /// <summary>
    /// The existing échéance this line revises, echoed back by the client. A row carrying collected money
    /// MUST be echoed back — dropping it would erase that cash from the plan's balance, and the domain
    /// refuses it.
    /// </summary>
    public Guid? Id { get; set; }

    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
}

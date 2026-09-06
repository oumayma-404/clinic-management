namespace ClinicManagement.Application.DTOs;

public class DentalRecordDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }

    /// <summary>
    /// The appointment this fiche documents, or null when it was entered outside the agenda. Read back so a screen can
    /// answer « cette séance a-t-elle déjà une fiche ? » — which nothing could, because the column was never populated.
    /// </summary>
    public Guid? AppointmentId { get; set; }

    public DateTime InterventionDate { get; set; }
    /// <summary>Derived summary of the acts' procedure names (read-only).</summary>
    public string ProcedureType { get; set; } = string.Empty;
    /// <summary>Derived total = sum of act costs (read-only).</summary>
    public decimal Cost { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>
    /// How <see cref="AmountPaid"/> was settled — <c>Cash</c>/<c>Cheque</c>/<c>Card</c>/<c>Transfer</c>, or null
    /// when nothing was recorded, which every read takes as cash. The cheque parts are null for any other method.
    /// </summary>
    public string? PaymentMethod { get; set; }

    /// <inheritdoc cref="PaymentMethod"/>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="PaymentMethod"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="PaymentMethod"/>
    public DateTime? ChequeDueDate { get; set; }

    public decimal Balance { get; set; } // derived: Cost − AmountPaid
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    public bool IsAdultTeeth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public List<DentalRecordActDto> Acts { get; set; } = new();
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// What happened to the money when this fiche was saved. Present on the create/update responses only — it is
    /// the outcome of a post-commit side effect, not stored state, so a later <c>GET</c> leaves it null.
    /// <para>
    /// It exists because the billing is best-effort for the <i>record</i> but must never be silent about the
    /// <i>cash</i>: a swallowed failure would put the user right back where they started, believing money was
    /// recorded when it was not.
    /// </para>
    /// </summary>
    public DentalRecordBillingDto? Billing { get; set; }

    /// <summary>
    /// What happened to money collected for the <b>treatment</b> this séance carries out — the second, separate
    /// figure, present on the create/update responses only for <see cref="Billing"/>'s reason.
    ///
    /// <para>
    /// ⚠️ <b>Two fields because they are two quantities, and no arithmetic is ever done between them.</b>
    /// « Payé » settles this séance's own acts and produces a note d'honoraires; « Encaissé sur le traitement »
    /// draws down a multi-séance act priced once on its devis, and produces an échéance payment. A mixed fiche —
    /// one devis act plus a filling done the same day — legitimately has both, and folding them into one number
    /// would put a share of the treatment's money on the note, which is the double-billing the devis act's 0 fee
    /// exists to prevent.
    /// </para>
    /// </summary>
    public TreatmentCollectionDto? TreatmentCollection { get; set; }

    /// <summary>
    /// What this séance actually collected onto the treatment it carries out — <b>read back from the échéancier
    /// ledger</b>, present on every read, unlike <see cref="TreatmentCollection"/> which reports one save.
    ///
    /// <para>
    /// ⚠️ <b>Derived, never stored.</b> A second copy of the figure on the fiche is exactly the trap
    /// <c>DentalRecord.AmountPaid</c> was: a field shaped like a receipt that no money read touches, free to
    /// disagree with the ledger the moment a payment is voided. The authority is
    /// <c>InstallmentPayment.DentalRecordId</c>, summed over the live rows.
    /// </para>
    /// <para>
    /// ⚠️ <b>Why it has to exist at all.</b> A séance of a multi-séance act is priced 0 on the fiche — the act is
    /// chiffré once, on the treatment — so <c>Cost</c> and <c>AmountPaid</c> are both 0 and the patient's fiche
    /// history printed « 0,000 DT » for three séances that had taken 1 000 DT between them. The money was
    /// correct on the treatment and invisible on the list of the visits that produced it.
    /// </para>
    /// </summary>
    public decimal? CollectedOnTreatment { get; set; }

    /// <summary>The treatment that money went to, so the row can name it. Null when the fiche collected none.</summary>
    public Guid? TreatmentPlanId { get; set; }

    /// <inheritdoc cref="TreatmentPlanId"/>
    public string? TreatmentPlanNumber { get; set; }
}

/// <summary>
/// The outcome of collecting on the treatment a séance carries out (see
/// <see cref="DentalRecordDto.TreatmentCollection"/>). Mirrors <see cref="DentalRecordBillingDto"/>'s shape,
/// deliberately: the fiche reports both the same way, and a caller handling one can handle the other.
/// </summary>
public class TreatmentCollectionDto
{
    /// <summary>A <c>TreatmentCollectionOutcome</c> name.</summary>
    public string Outcome { get; set; } = string.Empty;

    public Guid? TreatmentPlanId { get; set; }

    /// <summary>The devis number — possibly minted by this very save. See below.</summary>
    public string? PlanNumber { get; set; }

    /// <summary>What this save actually put on the échéancier — the increment, never the cumulative figure.</summary>
    public decimal? AmountCollected { get; set; }

    /// <summary>What the patient still owes on the treatment afterwards — what the next séance prefills from.</summary>
    public decimal? Outstanding { get; set; }

    /// <summary>
    /// True when this save is what gave the treatment its devis number. The UI must say so: a gapless number was
    /// consumed, and it can only be released by a cancellation carrying a motif.
    /// </summary>
    public bool DevisIssued { get; set; }

    /// <summary>The French reason, for <c>Refused</c>.</summary>
    public string? Message { get; set; }
}

/// <summary>What saving a fiche did about its « Montant payé ».</summary>
public enum DentalRecordBillingOutcome
{
    /// <summary>No payment on the fiche, so nothing was billed. Not an error.</summary>
    NotCollected = 0,

    /// <summary>A note d'honoraires was issued and the payment recorded.</summary>
    Billed = 1,

    /// <summary>The fiche was already on a live note, with nothing to add — the expected outcome of re-saving one.</summary>
    AlreadyBilled = 2,

    /// <summary>The record saved, the billing did not. The user has to be told.</summary>
    Failed = 3,

    /// <summary>
    /// « Montant payé » was raised on an already-billed fiche and the difference was recorded as an additional
    /// payment on the <b>same</b> note d'honoraires (AC-1).
    ///
    /// <para>This is the outcome the whole part exists for: re-saving a fiche with a higher amount used to be
    /// refused as « déjà facturée » and the extra cash simply never reached the till — a silent money leak on the
    /// most ordinary edit there is (« le patient a fini de payer »).</para>
    /// </summary>
    ToppedUp = 4,

    /// <summary>
    /// A rule said no: the amount was lowered, the acts changed after issue, or the note is cancelled/credited.
    /// Distinct from <see cref="Failed"/> — nothing went wrong, and the user has a defined next step (an avoir),
    /// which the message names.
    /// </summary>
    Refused = 5
}

/// <summary>The money outcome of a fiche save (see <see cref="DentalRecordDto.Billing"/>).</summary>
public class DentalRecordBillingDto
{
    /// <summary>A <see cref="DentalRecordBillingOutcome"/> name.</summary>
    public string Outcome { get; set; } = string.Empty;

    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? AmountCollected { get; set; }

    /// <summary>The French reason, for <c>Failed</c> and <c>AlreadyBilled</c>.</summary>
    public string? Message { get; set; }
}

/// <summary>One act on a dental record (procedure + teeth + cost + resulting odontogram state).</summary>
public class DentalRecordActDto
{
    public Guid Id { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    /// <summary>The act's total fee (authoritative).</summary>
    public decimal Cost { get; set; }
    /// <summary>Per-unit price <see cref="Cost"/> was built from; null when never captured (legacy rows).</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>True when <see cref="Cost"/> is <see cref="UnitCost"/> × teeth; false = flat fee.</summary>
    public bool IsPerTooth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public string? ResultingCondition { get; set; }
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}

/// <summary>One requested act when creating/updating a dental record.</summary>
public class DentalActInput
{
    public Guid? ProcedureTypeId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    /// <summary>The act's total fee. The server stores it as sent — it is never recomputed from the unit price.</summary>
    public decimal Cost { get; set; }
    /// <summary>Optional per-unit price the total was built from (pricing provenance for the editor + invoice).</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>Whether <see cref="Cost"/> is per treated tooth (else a flat session fee). Ignored when no teeth.</summary>
    public bool IsPerTooth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    /// <summary>Resulting odontogram state (ToothCondition name); null/empty/"Sain" = no odontogram entry.</summary>
    public string? ResultingCondition { get; set; }
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}

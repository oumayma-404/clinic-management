namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Which ledger a caisse movement came from. The four are the <b>whole</b> of the clinic's cash: there is no
/// fifth source, and adding one means adding a member here rather than a parallel read.
/// <para>
/// ⚠️ A fiche de soins is deliberately <b>not</b> a member. `DentalRecord.AmountPaid` is not cash — no receipt,
/// no number, no void path — and a session's payment reaches the till by raising a note d'honoraires, which
/// arrives as an <see cref="InvoicePayment"/> like any other.
/// </para>
/// </summary>
public enum CaisseMovementKind
{
    /// <summary>A payment recorded against a note d'honoraires.</summary>
    InvoicePayment = 0,

    /// <summary>A collection against a devis échéance (the event-sourced installment ledger).</summary>
    InstallmentPayment = 1,

    /// <summary>An avoir refunded to the patient — money out.</summary>
    Refund = 2,

    /// <summary>A clinic expense — money out.</summary>
    Expense = 3
}

/// <summary>Which way the money went. Explicit rather than inferred from the sign of <c>Amount</c>.</summary>
public enum CaisseMovementDirection
{
    In = 0,
    Out = 1
}

/// <summary>
/// One line of the « extrait de caisse » — the statement behind the caisse's three totals.
///
/// <para><b>Why this is a projection and not a table.</b> Every field here is derived from a row that already
/// exists in one of the four ledgers. A `CashMovement` table written by each money path would be double
/// bookkeeping: the day one write site forgets, the statement and the totals disagree and nothing can say which
/// is right. Reading the same rows the totals sum makes `Σ movements == cashIn − refunds − cashOut` an
/// assertion a test can hold — which is the one guarantee that matters on a screen a clinic reconciles against.
/// </para>
///
/// <para><b>Voided rows appear and do not count.</b> § 1 decided a void keeps the row and strikes it through
/// (a reprinted receipt is stamped « REÇU ANNULÉ »). Hiding them here would make the statement useless as the
/// audit trail it is meant to be, so they are returned with their motif and actor, excluded from
/// <see cref="RunningBalance"/>, and excluded from every total.</para>
/// </summary>
public class CaisseMovementDto
{
    /// <summary>The underlying ledger row's id. Unique within its kind, not across kinds.</summary>
    public Guid Id { get; set; }

    public CaisseMovementKind Kind { get; set; }

    public CaisseMovementDirection Direction { get; set; }

    /// <summary>
    /// The date the movement is <b>attributed to</b> — <c>PaidOn</c> / <c>RefundedOn</c> / <c>ExpenseDate</c>,
    /// never <c>CreatedAt</c>. A payment back-dated to yesterday belongs to yesterday's till.
    /// </summary>
    public DateTime OccurredOn { get; set; }

    /// <summary>Always positive. The direction carries the sign, so a reader cannot mistake a refund for income.</summary>
    public decimal Amount { get; set; }

    /// <summary>`PaymentMethod` name, or null for a movement with no recorded means (a legacy avoir).</summary>
    public string? Method { get; set; }

    /// <summary>
    /// The French one-line description (« Paiement facture 2026-0012 », « Dépense — Consommables »). Built
    /// server-side, once: four kinds × the client would be four copies of the wording to keep in step.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The document number this movement belongs to, when it has one (a draft invoice has none).</summary>
    public string? Reference { get; set; }

    public Guid? PatientId { get; set; }
    public string? PatientName { get; set; }

    /// <summary>
    /// The aggregate to open on click — the invoice, the devis, the invoice the avoir credits, or the expense.
    /// Which route that is belongs to the frontend (`Kind` says which), the same split as `dashboard-links.ts`.
    /// </summary>
    public Guid? TargetId { get; set; }

    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedByName { get; set; }

    /// <summary>
    /// Cumulative net across the returned window, oldest → newest, skipping voided rows.
    /// <para>
    /// ⚠️ <b>Window-relative</b>: it opens at zero on the first line of the selected range, and is not an
    /// all-time cash balance. The UI must label it as « Solde de la période » — a column called « Solde » next
    /// to bank-statement-shaped rows reads as an account balance, which this is not.
    /// </para>
    /// </summary>
    public decimal RunningBalance { get; set; }
}

/// <summary>
/// The statement plus the window it covers. Carries no totals of its own **on purpose**: they live in
/// <see cref="CaisseSummaryDto"/>, and a second copy computed here is a second answer to the same question.
/// </summary>
public class CaisseLedgerDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<CaisseMovementDto> Movements { get; set; } = new();
}

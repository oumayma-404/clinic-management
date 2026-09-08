namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The unified per-patient money view (« Solde patient ») — the single balance across both billing tracks
/// (issued invoices + treatment-plan installments) plus the indicative CNAM split over everything billed.
/// All amounts in TND. Computed on read; never persisted.
/// </summary>
public class PatientBillingSummaryDto
{
    /// <summary>Outstanding across the patient's issued, non-cancelled invoices (Σ TTC − collected).</summary>
    public decimal InvoiceOutstanding { get; set; }

    /// <summary>Outstanding across the patient's non-cancelled treatment-plan installments (Σ amount − paid).</summary>
    public decimal InstallmentOutstanding { get; set; }

    /// <summary>The single « Solde patient » = invoice outstanding + installment outstanding.</summary>
    public decimal TotalOutstanding { get; set; }

    /// <summary>Oldest overdue installment due date (unpaid, past due), or null if nothing is overdue.</summary>
    public DateTime? OldestOverdueDate { get; set; }

    /// <summary>Indicative CNAM-reimbursable portion across everything billed to the patient.</summary>
    public decimal CnamReimbursable { get; set; }

    /// <summary>Patient out-of-pocket (reste à charge) = total billed − CNAM-reimbursable.</summary>
    public decimal PatientOutOfPocket { get; set; }

    /// <summary>
    /// Total refunded to this patient through avoirs, across their invoices. Informational: an avoir returns
    /// collected cash <b>and</b> cancels the corresponding fee, so the net position — and therefore
    /// <see cref="TotalOutstanding"/> — is unchanged by it. Shown so a « Solde patient » of 0 after a refund
    /// is legible as "settled, 300 DT returned" rather than looking like nothing ever happened.
    /// </summary>
    public decimal CreditedTotal { get; set; }

    /// <summary>
    /// What <see cref="TotalOutstanding"/> is made of — one row per document that still owes, and the whole of
    /// it. « Solde dû » stood on the patient file as a single figure with nothing saying what it covered, and
    /// the composition lived in two places the file could not show side by side: the notes d'honoraires in the
    /// seventh tab, the devis échéanciers only on <c>/treatment-plans/{id}</c>.
    ///
    /// <para><b>Served, never derived in the browser</b> — the rule <c>/factures</c> already states for its own
    /// « dont … sur des devis »: only the server can apply <see cref="Domain.Services.PlanBillingRules"/>, and a
    /// client that summed the two lists it happens to have loaded would count a bridged devis on both tracks.
    /// Measured once already, on the plan side: 4 of 4 bridged plans reported the whole devis as unpaid, two of
    /// them fully settled.</para>
    ///
    /// <para>⚠️ <b>One row per DOCUMENT, never per échéance</b>, and the total is derived from these rows rather
    /// than computed beside them. A plan's outstanding is <c>TotalPlanned − Σ AmountPaid</c> while the échéancier
    /// sums to <c>Σ (Amount − AmountPaid)</c>, and <c>reconcile-money</c>'s <c>plan-schedule-balances</c> exists
    /// because <c>AddItems</c>/<c>RemoveItem</c> can pull the two apart. Itemising by échéance would print rows
    /// that do not add up to the figure above them.</para>
    /// </summary>
    public List<PatientDebtLineDto> Lines { get; set; } = new();
}

/// <summary>
/// One document a patient still owes on — a note d'honoraires or a devis — carrying what it is, what work it
/// covers, and (for a devis) which échéance a payment should land on.
/// </summary>
/// <param name="Kind">
/// <c>Invoice</c> or <c>TreatmentPlan</c>. ⚠️ Branch on this, never on <paramref name="Label"/>: a
/// <c>Contains("devis")</c> once made rewording a sentence change behaviour.
/// </param>
/// <param name="DocumentId">The note or the devis. What the client re-reads before opening a payment dialog.</param>
/// <param name="Number">« 2026-0042 ». Never null here — an un-numbered document carries no debt.</param>
/// <param name="Label">« Note d'honoraires » / « Devis », for the row's own line.</param>
/// <param name="Covers">
/// The acts this document bills, comma-joined — « Détartrage, Couronne 26 ». The honest granularity: a payment
/// is recorded against a document and never against a line, so this NAMES the work without claiming which act
/// of it is unpaid.
/// </param>
/// <param name="Total">The document's own total.</param>
/// <param name="Collected">What has been received against it.</param>
/// <param name="Outstanding">What is still owed. Floors at 0, so an overpaid document never appears.</param>
/// <param name="Since">
/// When the debt started — a note's issue date, a devis's oldest unpaid échéance. Null when the document
/// carries no date to say it with, in which case the row states no age rather than inventing one.
/// </param>
/// <param name="IsOverdue">
/// ⚠️ <b>A devis échéance only.</b> Served from <c>InstallmentLateness</c>, the one authority. A note
/// d'honoraires is payable on issue, so « en retard » would be true of every unpaid note the day after it was
/// raised — which is exactly the defect that made 25 of 27 échéances read « En retard » before that rule
/// existed. A note carries its <b>age</b> instead, which is a fact rather than a policy nobody set.
/// </param>
/// <param name="PayableInstallmentId">
/// A devis only: the oldest unpaid échéance, which is what « Encaisser » targets. <b>Null means the devis has
/// no échéance able to take the money</b> — an empty échéancier (« le patient paie en une fois ») or one that no
/// longer sums to the plan total — and the row must then offer the devis rather than a payment dialog with no
/// target. See <paramref name="PayableRoom"/>.
/// </param>
/// <param name="PayableRoom">
/// How much the échéancier can actually accept, i.e. <c>Σ (Amount − AmountPaid)</c> over the devis's unpaid
/// échéances. Equal to <paramref name="Outstanding"/> in the ordinary case. When it is <b>less</b>, the row has
/// to say so: <c>Installment.RecordPayment</c> is bounded by one échéance's own room, so offering the row's
/// figure would produce a refusal for the number the row had just printed.
/// </param>
public sealed record PatientDebtLineDto(
    string Kind,
    Guid DocumentId,
    string? Number,
    string Label,
    string Covers,
    decimal Total,
    decimal Collected,
    decimal Outstanding,
    DateTime? Since,
    bool IsOverdue,
    Guid? PayableInstallmentId,
    decimal PayableRoom);

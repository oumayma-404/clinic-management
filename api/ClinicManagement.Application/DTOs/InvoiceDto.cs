namespace ClinicManagement.Application.DTOs;

public class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid? DentalRecordId { get; set; }
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// Which practitioner earned this note (L9). <b>Null is a real answer</b> — a historical row, or one raised with
    /// no practitioner in scope — and a client must render it as « non attribué » rather than as the clinic.
    /// </summary>
    public Guid? DoctorId { get; set; }

    /// <summary>
    /// The practitioner's name, resolved by the read. Carried beside the id for the same reason
    /// <c>PatientName</c> is: <c>Invoice</c> has no navigation the list read materialises, so the alternative is a
    /// lookup per row on the screen a clinic opens to chase money.
    /// </summary>
    public string? DoctorName { get; set; }

    /// <summary>The devis this note was bridged from (devis→facture), or null for a standalone note. Lets
    /// « Factures » mark a devis-born invoice and navigate back to the plan it represents.</summary>
    public Guid? TreatmentPlanId { get; set; }

    public string? Number { get; set; }
    public DateTime? IssueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }
    public decimal StampDutyAmount { get; set; }
    public string? CancellationReason { get; set; }
    public decimal TotalHt { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalTtc { get; set; }
    public decimal AmountCollected { get; set; }
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
    /// Server-computed. The frontend used to re-derive this from status + amountCollected, which is exactly
    /// how it ended up offering « Annuler » on invoices the API refuses — after a full void the status is
    /// Issued and collected is 0, but the voided payment rows are still there.
    /// </summary>
    public bool CanCancel { get; set; }

    /// <summary>Server-computed, for the same reason as <see cref="CanCancel"/>.</summary>
    public bool CanCreateAvoir { get; set; }

    /// <summary>
    /// Server-computed. Whether this note can be replaced by a corrected one — the route for a mistake, as
    /// opposed to <see cref="CanCreateAvoir"/>, which is the route for money actually handed back.
    /// </summary>
    public bool CanBeCorrected { get; set; }

    /// <summary>The note that replaced this one after a correction, so a cancelled note is not a dead end.</summary>
    public Guid? SupersededByInvoiceId { get; set; }

    /// <summary>The note this one corrects. Set on a correction draft and on the note it becomes.</summary>
    public Guid? SupersedesInvoiceId { get; set; }

    /// <summary>
    /// Sum of the avoirs established against this invoice. Always populated (0 when there are none) so the
    /// list can badge a credited invoice; until now an avoir was invisible everywhere after creation.
    /// </summary>
    public decimal CreditedTotal { get; set; }

    public List<InvoiceLineDto> Lines { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();

    /// <summary>
    /// The avoirs themselves — only populated on the single-invoice read (the detail modal), not on the list,
    /// which gets <see cref="CreditedTotal"/> alone.
    /// </summary>
    public List<CreditNoteDto> CreditNotes { get; set; } = new();
}

public class InvoiceLineDto
{
    public Guid Id { get; set; }
    public string Designation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceHt { get; set; }
    public decimal LineTotalHt { get; set; }
    public Guid? DentalRecordId { get; set; }

    /// <summary>Optional catalog CNAM/DCH act this line bills (drives the reimbursable split); null = free-text.</summary>
    public Guid? DentalActCodeId { get; set; }
    public string? CodeActe { get; set; }
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// A voided payment is one that was never really received. The row is kept and shown struck through with
    /// its motif, so the correction leaves a trail rather than silently disappearing.
    /// </summary>
    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedByName { get; set; }

    /// <summary>Set when this payment was carried onto the invoice from a treatment-plan installment.</summary>
    public Guid? SourceInstallmentPaymentId { get; set; }
}

/// <summary>Aggregate revenue for the Recettes view: invoiced / collected / outstanding (TND).</summary>
public class InvoiceRevenueDto
{
    public decimal TotalInvoiced { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal Outstanding { get; set; }

    /// <summary>
    /// How much of <see cref="TotalCollected"/> was collected on <b>devis échéances</b> rather than on a note
    /// d'honoraires — money that is real, in la caisse, and has <b>no row on the invoices screen</b>.
    ///
    /// <para>
    /// ⚠️ <b>Served rather than derived, because the browser cannot compute it and had been left to guess.</b>
    /// « Total encaissé » deliberately counts both money tracks (so it agrees with la caisse and the dashboard)
    /// while the table beneath it lists the invoice ledger alone, and the screen said so only in prose:
    /// « paiements de notes et échéances de devis », with no figure. That is unreconcilable — a practice adding
    /// up the « Encaissé » column comes out short and cannot tell by how much or where the rest went. Measured
    /// on the dev database: 2 050,000 DT across seven payments, none of them visible anywhere on that page.
    /// </para>
    ///
    /// <para>
    /// It is a <b>component of</b> <c>TotalCollected</c>, never an addition to it: a caller that sums the two
    /// double-counts. Zero is the ordinary case — a practice that raises a note for everything — and the
    /// surface says nothing at all then.
    /// </para>
    /// </summary>
    public decimal CollectedOnTreatmentPlans { get; set; }
}

/// <summary>One requested act line when creating/updating an invoice.</summary>
public class InvoiceLineRequest
{
    public string Designation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceHt { get; set; }

    /// <summary>Optional dental record this line bills (drives the "already invoiced" guard, FR-1.2).</summary>
    public Guid? DentalRecordId { get; set; }

    /// <summary>Optional catalog CNAM/DCH act this line bills (drives the reimbursable split); null = free-text.</summary>
    public Guid? DentalActCodeId { get; set; }
    public string? CodeActe { get; set; }
}

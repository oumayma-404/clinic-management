namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One entry of the cabinet's subscription ledger, as the payment history renders it (AC-2.3).
///
/// <para><b>The period covered is derived, not stored.</b> <see cref="FromDay"/>/<see cref="ThroughDay"/> come out
/// of <c>SubscriptionLedger.FoldWithSpans</c> — the same fold that produces the end date the product enforces on —
/// so a row cannot claim a stretch of time the entitlement does not agree with. A <b>cancelled</b> entry gets no
/// span at all (both null): it contributes nothing and is shown struck through with its reason (AC-5.5).</para>
/// </summary>
public class SubscriptionPeriodDto
{
    public Guid Id { get; set; }

    /// <summary>`Trial` | `Paid` | `Grandfathered` | `Complimentary` — why the cabinet was covered.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>« Essai gratuit » | « Paiement » | « Antériorité » | « Offert ».</summary>
    public string KindLabel { get; set; } = string.Empty;

    /// <summary>Inclusive first day of the cover this entry opened. Null for a cancelled entry.</summary>
    public DateTime? FromDay { get; set; }

    /// <summary>Inclusive last day. Null for a cancelled entry <b>and</b> for an open-ended one — « sans échéance ».</summary>
    public DateTime? ThroughDay { get; set; }

    /// <summary>
    /// What the vendor was paid.
    ///
    /// <para>⚠️ <b>Never the clinic's money</b> (FR-2): none of this reaches la caisse, l'extrait, « Créances », the
    /// dashboard's Argent section or any patient's balance.</para>
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>`Transfer` | `Cash` | `Cheque` | `Card`, or null. Deliberately not the clinic's `PaymentMethod`.</summary>
    public string? Method { get; set; }

    /// <summary>« Virement » | « Espèces » | « Chèque » | « Carte », or null.</summary>
    public string? MethodLabel { get; set; }

    /// <summary>The transfer reference, cheque number or receipt number, as the vendor recorded it.</summary>
    public string? Reference { get; set; }

    // ⚠️ `Note` and `RecordedBy` are deliberately NOT on this DTO. `--note` is the vendor's own commentary about
    // the customer (« geste commercial », « pilote ») and `RecordedBy` publishes our internal command vocabulary;
    // neither is rendered by either tree of the history table, so shipping them was exposure with no product
    // benefit. They stay on `SubscriptionReportEntry`, which the vendor reads on their own console.

    public DateTime RecordedAt { get; set; }

    public bool IsCancelled { get; set; }

    public DateTime? CancelledAt { get; set; }

    /// <summary>Mandatory when cancelled — the end date can move into the past as a result (EC-4).</summary>
    public string? CancelReason { get; set; }
}

/// <summary>
/// One page of the ledger, newest entry first.
///
/// <para><b>Paged in memory, and that is the one shape <c>PagedResult.FromSource</c> is for.</b> The « période
/// couverte » of an entry is a function of every non-cancelled entry recorded before it, so no SQL window can know
/// a row's span — the whole ledger is folded, then a page is cut.</para>
/// </summary>
public class SubscriptionHistoryPageDto
{
    public List<SubscriptionPeriodDto> Items { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}

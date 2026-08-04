namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Chèques à encaisser » (L8 slice B) — every cheque the clinic holds, soonest-due first, over <b>both</b>
/// payment ledgers. A post-dated cheque nobody takes to the bank is money lost, and before this the number, the
/// bank and the due date existed on the rows with no screen anywhere that listed them.
///
/// <para>
/// ⚠️ <b>A cheque leaves this list only by being voided.</b> The product records no « encaissé en banque » event,
/// so a cheque banked last year is still listed — which is why the groups below are the load-bearing part of the
/// shape and the list is ordered by due date: what an owner needs is « which are due now », not « which exist ».
/// Recording the banking is its own feature (a column, a command and a write path); pretending otherwise by
/// silently dropping old rows would lose exactly the forgotten cheque the screen is for.
/// </para>
/// </summary>
public class ChequesDueDto
{
    /// <summary>The cheques on the requested page, soonest-due first with undated ones last.</summary>
    public List<ChequeDto> Items { get; set; } = new();

    /// <summary>
    /// Counts and totals over <b>every</b> matching cheque, not over <see cref="Items"/> — the same rule
    /// « Créances » follows for its header total. Summing the page would present one page's worth of cheques as
    /// the clinic's whole exposure.
    /// </summary>
    public ChequeGroupsDto Groups { get; set; } = new();

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// The four buckets a cheque can be in, as of the clinic's own today. Every cheque falls into exactly one, so the
/// four counts sum to <see cref="ChequesDueDto.TotalCount"/> and the four totals sum to
/// <see cref="ChequeGroupsDto.Total"/>.
/// </summary>
public class ChequeGroupsDto
{
    /// <summary>Due date already past — presentable now, and the ones costing the clinic money by sitting in a drawer.</summary>
    public ChequeBucketDto Overdue { get; set; } = new();

    /// <summary>Due within the next 30 clinic-local days.</summary>
    public ChequeBucketDto DueSoon { get; set; } = new();

    /// <summary>Due later than 30 days.</summary>
    public ChequeBucketDto Later { get; set; } = new();

    /// <summary>
    /// No due date recorded. ⚠️ Its own counted group, deliberately: the field stays optional even for a cheque
    /// (refusing money genuinely received to enforce one is the wrong trade), so this is the bucket a cheque falls
    /// into when nobody wrote the date down — and therefore the one nobody would ever chase. Counting it is the
    /// whole reason it is not simply sorted to the end and forgotten.
    /// </summary>
    public ChequeBucketDto Undated { get; set; } = new();

    /// <summary>Every cheque held, whatever its bucket.</summary>
    public ChequeBucketDto Total { get; set; } = new();
}

public class ChequeBucketDto
{
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>One cheque held by the clinic, from either payment ledger.</summary>
public class ChequeDto
{
    /// <summary>The payment row's id — the <c>Payment</c> or the <c>InstallmentPayment</c>, per <see cref="Kind"/>.</summary>
    public Guid Id { get; set; }

    /// <summary><c>InvoicePayment</c> or <c>InstallmentPayment</c> — the same vocabulary as <c>CaisseMovementKind</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Which bucket of <see cref="ChequeGroupsDto"/> this row is in — computed server-side, so the list and the counts above it cannot disagree.</summary>
    public string Bucket { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>The day the cheque was handed over — <b>not</b> when it can be banked.</summary>
    public DateTime ReceivedOn { get; set; }

    /// <summary>The day it may be presented. Null when nobody recorded one; that is a counted case, never a dropped row.</summary>
    public DateTime? DueDate { get; set; }

    public string? ChequeNumber { get; set; }
    public string? BankName { get; set; }

    /// <summary>The note d'honoraires or devis number the cheque paid, when it has one (a draft invoice does not).</summary>
    public string? Reference { get; set; }

    public Guid? PatientId { get; set; }
    public string? PatientName { get; set; }

    /// <summary>The aggregate to open — the invoice, or the devis for an échéance.</summary>
    public Guid TargetId { get; set; }
}

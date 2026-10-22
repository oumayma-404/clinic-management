namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A note, devis or avoir number the cloud promised this PC de secours before committing it (<c>clinic-pc-copy</c> D16,
/// FR-3, EC-22). Kept on the PC only: when the PC takes over, it numbers after the highest number it was promised as well
/// as the highest it holds — so a number the cloud issued seconds before a cut, whose row never arrived, is never issued
/// twice. Carries the save's <c>Idempotency-Key</c> (D17), what a re-press across a switch reuses (Part 3).
///
/// <para>Written in raw SQL by the PC's promise loop; never copied, never archived. Always empty on the cloud.</para>
/// </summary>
public class RelayNumberPromise
{
    public const string InvoiceSequence = "invoice";
    public const string DevisSequence = "devis";
    public const string CreditNoteSequence = "credit-note";

    public Guid ClinicId { get; private set; }

    /// <summary><see cref="InvoiceSequence"/>, <see cref="DevisSequence"/> or <see cref="CreditNoteSequence"/>.</summary>
    public string Sequence { get; private set; } = string.Empty;

    /// <summary>The number as issued, <c>AAAA-NNNN</c>.</summary>
    public string Number { get; private set; } = string.Empty;

    public string? IdempotencyKey { get; private set; }
    public DateTime PromisedAtUtc { get; private set; }

    private RelayNumberPromise() { }

    /// <summary>The <c>NNNN</c> of an <c>AAAA-NNNN</c> number in <paramref name="year"/>, or 0 for another year or shape.</summary>
    public static int SequenceOf(string? number, int year)
    {
        var prefix = $"{year}-";
        return number is not null && number.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(number.AsSpan(prefix.Length), out var n) && n > 0
            ? n
            : 0;
    }
}

using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// What identifies a cheque: its number, the drawing bank, and the date it may be banked (L8).
///
/// <para><b>Why this exists at all.</b> Post-dated cheques are ubiquitous in Tunisian private practice, and
/// <see cref="PaymentMethod.Cheque"/> was a <b>bare enum value</b>: <see cref="Entities.Payment"/>,
/// <see cref="Entities.InstallmentPayment"/>, <c>CreditNote</c> and <c>Expense</c> each carried
/// <c>Amount</c>/<c>Method</c>/<c>PaidOn</c> and nothing else. For money *out* the number can go in an expense's
/// description; for money **in** there was no free-text field of any kind — so « quel chèque ? de quelle banque ?
/// encaissable quand ? » had nowhere to live, and a post-dated cheque nobody banks is simply money lost.</para>
///
/// <para><b>Why a value object and not three loose properties.</b> Two ledgers need the same three fields under the
/// same rule, and the rule is the load-bearing part: cheque details on a <i>cash</i> payment are data nothing can
/// interpret, and they would make a « chèques à encaisser » view list a payment that is not a cheque. Written as
/// six nullable columns with the guard at each call site, the second write path to be added would be the one that
/// forgets — the failure shape this repo has documented a dozen times. Here there is one guard, in
/// <see cref="For"/>, and no way to reach the entities without passing through it.</para>
///
/// <para>⚠️ It is <b>not</b> an EF-owned type: both entities flatten it into three plain nullable columns. A cheque
/// number is read, searched and filtered on its own (the « chèques à encaisser » view keys on
/// <see cref="DueDate"/>), and an owned type inside what is already an aggregate child buys nothing for that while
/// making every projection nest.</para>
/// </summary>
public sealed class ChequeDetails : ValueObject
{
    /// <summary>The cheque's number as written on it. Free text — formats vary by bank.</summary>
    public string? Number { get; }

    /// <summary>The drawing bank, as the patient's cheque names it. Free text: there is no bank registry here.</summary>
    public string? BankName { get; }

    /// <summary>
    /// The date the cheque may be presented — the whole point of recording it. A <b>calendar day</b>, stored with
    /// no zone conversion, exactly like an échéance's due date: converting it would move it by a day for half the
    /// values, and « encaissable le 3 » is a fact about a paper document, not an instant.
    /// </summary>
    public DateTime? DueDate { get; }

    /// <summary>Nothing was recorded — the caller may as well have passed null.</summary>
    public bool IsEmpty => Number == null && BankName == null && DueDate == null;

    private ChequeDetails(string? number, string? bankName, DateTime? dueDate)
    {
        Number = number;
        BankName = bankName;
        DueDate = dueDate;
    }

    /// <summary>
    /// The details for a payment made by <paramref name="method"/>, or <c>null</c> when nothing was supplied.
    ///
    /// <para>All three parts stay <b>optional even for a cheque</b>: reception could previously record a cheque
    /// with one field, and refusing money that was genuinely received in order to enforce a field is the wrong
    /// trade. The consequence is deliberate and handled downstream rather than here — a cheque with no
    /// <see cref="DueDate"/> cannot be sorted into a « à encaisser » date, so it is counted and listed as its own
    /// group rather than silently dropped, which would hide exactly the rows that view exists for.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When details are supplied for a method that is not <see cref="PaymentMethod.Cheque"/>. Refused rather than
    /// ignored: silently discarding them loses what the user typed, and silently *keeping* them would put a cheque
    /// number on a cash payment, which every read would then have to second-guess.
    /// </exception>
    public static ChequeDetails? For(PaymentMethod method, string? number, string? bankName, DateTime? dueDate)
    {
        var trimmedNumber = Normalize(number);
        var trimmedBank = Normalize(bankName);

        if (trimmedNumber == null && trimmedBank == null && dueDate == null)
        {
            return null;
        }

        if (method != PaymentMethod.Cheque)
        {
            throw new ArgumentException(
                "Les informations de chèque (numéro, banque, date d'échéance) ne s'appliquent qu'à un paiement par chèque.",
                nameof(method));
        }

        return new ChequeDetails(trimmedNumber, trimmedBank, dueDate);
    }

    /// <summary>Blank is blank — an untouched form field must not store an empty string a read then has to test for.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Number ?? string.Empty;
        yield return BankName ?? string.Empty;
        yield return DueDate ?? default(DateTime);
    }
}

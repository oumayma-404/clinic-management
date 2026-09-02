using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A dépense that repeats every month — a loyer, a salaire, the monthly instalment of a credit. It is a
/// <b>forward-looking instruction</b>, not a fact about the past: the <see cref="Expense"/> rows it has already
/// posted are ordinary dépenses that belong to their own month and are never touched by anything done here.
///
/// <para><b>Why it is a template and not a flag on <see cref="Expense"/>.</b> « Modifier » has to mean « le loyer
/// est passé à 850 » from next month on, and « Arrêter » has to mean « le crédit est soldé » — neither is
/// expressible as an edit to a row that is also one month's money. Keeping the instruction separate is what lets
/// a posted occurrence stay an ordinary, correctable dépense.</para>
///
/// <para><b><see cref="LastPostedMonth"/> is the authority, not the rows.</b> The posting pass advances it per
/// month written, so deleting a posted dépense does not make the pass re-create it — a deletion is a decision
/// about that month's money, not a request to repeat the month.</para>
/// </summary>
public class RecurringExpense : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string Category { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Which day of the month it falls on. A 29–31 is clamped to a shorter month's last day when posting.</summary>
    public int DayOfMonth { get; private set; }

    /// <summary>
    /// The <c>AAAA-MM</c> key of the last month posted — see <c>ClinicClock.MonthKey</c>, which the Domain cannot
    /// reference. Set at creation to the month of the dépense that started the series, so the pass can never
    /// reach back before the series existed.
    /// </summary>
    public string LastPostedMonth { get; private set; }

    /// <summary>Set by <see cref="Stop"/>. A stopped series is never deleted and never posts again.</summary>
    public DateTime? CancelledAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public bool IsActive => CancelledAt == null;

    private RecurringExpense() { } // For EF Core

    public RecurringExpense(
        Guid id,
        Guid clinicId,
        string category,
        decimal amount,
        PaymentMethod method,
        int dayOfMonth,
        string lastPostedMonth,
        string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de la dépense doit être supérieur à 0.", nameof(amount));
        if (dayOfMonth is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Le jour du mois doit être compris entre 1 et 31.");

        Id = id;
        ClinicId = clinicId;
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Amount = amount;
        Method = method;
        DayOfMonth = dayOfMonth;
        LastPostedMonth = lastPostedMonth ?? throw new ArgumentNullException(nameof(lastPostedMonth));
        Description = description;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Changes what FUTURE months will post. <see cref="LastPostedMonth"/> is deliberately untouched: an edit is
    /// not a request to re-post a month, and every occurrence already in la caisse keeps the figure it was
    /// recorded with.
    /// </summary>
    public void Update(string category, decimal amount, PaymentMethod method, int dayOfMonth, string? description)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de la dépense doit être supérieur à 0.", nameof(amount));
        if (dayOfMonth is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Le jour du mois doit être compris entre 1 et 31.");

        Category = category ?? throw new ArgumentNullException(nameof(category));
        Amount = amount;
        Method = method;
        DayOfMonth = dayOfMonth;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a month as posted. Advances only forwards, so a re-run cannot rewind the marker.</summary>
    public void MarkPosted(string monthKey)
    {
        if (string.CompareOrdinal(monthKey, LastPostedMonth) > 0)
        {
            LastPostedMonth = monthKey;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Ends the series. Idempotent, and it does not settle up: a month still unposted when this is called stays
    /// unposted, because « arrêter » is what a practice says when the commitment is over.
    /// </summary>
    public void Stop(DateTime cancelledAtUtc)
    {
        if (CancelledAt == null)
        {
            CancelledAt = cancelledAtUtc;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}

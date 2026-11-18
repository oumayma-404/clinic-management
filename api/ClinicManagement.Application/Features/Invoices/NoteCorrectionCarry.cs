using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>One payment of a corrected note re-recorded on its replacement — which row, and how much of it.</summary>
public sealed record CarriedPayment(Guid PaymentId, decimal Amount);

/// <summary>
/// What « Corriger la note », started from the fiche de soins, carries from the note it retires onto the note it
/// raises (`seance-au-fauteuil`).
///
/// <para><b>Why it exists.</b> The correction voided every payment on the note and re-billed the séance with ONE
/// payment worth the fiche's « Payé », in the fiche's own method and on the séance's date. That was harmless while
/// only the dentist took money, at the chair. Once the desk takes it, it erased the desk's rows: a cheque handed in
/// at reception came back as cash with no number and no bank — gone from « Chèques à encaisser » — and money taken
/// on another day moved to the séance's day in la caisse.</para>
///
/// <para><b>The rule.</b> The live rows are carried in the order they were received, each clamped to what is left
/// of the corrected « Payé », with their own date, method, cheque identity and bank mark — so every day of la caisse
/// nets to zero and a cheque stays the same cheque. Money above the corrected « Payé » is what the dentist says was
/// never received, which is the correction's purpose: « le patient n'a pas payé » carries nothing. Same order and
/// same clamp as the note's own « Corriger » (<c>IssueInvoiceCommand</c>), with the fiche's « Payé » as the ceiling
/// instead of the note's total.</para>
/// </summary>
public static class NoteCorrectionCarry
{
    /// <param name="livePayments">The retired note's payments; voided rows are ignored.</param>
    /// <param name="amountPaid">The fiche's corrected « Payé » — the most the replacement may carry.</param>
    public static IReadOnlyList<CarriedPayment> Plan(IEnumerable<Payment> livePayments, decimal amountPaid)
    {
        var remaining = InvoiceCalculator.RoundMoney(Math.Max(0m, amountPaid));
        var carried = new List<CarriedPayment>();
        foreach (var payment in livePayments
                     .Where(p => !p.IsVoided)
                     .OrderBy(p => p.PaidOn)
                     .ThenBy(p => p.CreatedAt)
                     .ThenBy(p => p.Id))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var amount = InvoiceCalculator.RoundMoney(Math.Min(payment.Amount, remaining));
            carried.Add(new CarriedPayment(payment.Id, amount));
            remaining = InvoiceCalculator.RoundMoney(remaining - amount);
        }

        return carried;
    }

    /// <summary>
    /// A deposited cheque the plan would not carry in full, or null. It is at the bank, so voiding it would
    /// un-receive money the bank statement says arrived — refused before anything is written, like re-dating such a
    /// séance (L4).
    /// </summary>
    public static Payment? StrandedBankedCheque(IEnumerable<Payment> livePayments, IReadOnlyList<CarriedPayment> plan)
    {
        var carried = plan.ToDictionary(c => c.PaymentId, c => c.Amount);
        return livePayments.FirstOrDefault(p =>
            !p.IsVoided
            && p.ChequeBankedOn is not null
            && (!carried.TryGetValue(p.Id, out var amount) || amount < InvoiceCalculator.RoundMoney(p.Amount)));
    }
}

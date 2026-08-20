using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders;

/// <summary>
/// The single posting of « le travail est arrivé, donc il a été acheté » into la caisse, shared by the status
/// path and the details path.
///
/// <para>Shared rather than written once on purpose, and for the same reason as
/// <see cref="LabOrderAppointmentLink"/>: <b>there are two doors onto this rule.</b> Marking a bon « Reçu » is
/// the obvious one. The other is a bon that arrives before the laboratory's invoice does — received with no
/// coût, then edited to enter it — and a rule wired only to the status transition would leave that bon owing a
/// dépense forever with nothing left to post it. Both doors call this.</para>
///
/// <para>Idempotency is <see cref="LabWorkOrder.NeedsCaisseExpense"/>'s job, not this method's: it is keyed on
/// <c>ExpenseId</c> rather than on the transition, so a piece that goes back to the lab and arrives again is
/// charged once.</para>
/// </summary>
public static class LabOrderCaisseExpense
{
    /// <summary>
    /// The caisse catégorie a bon de prothèse is filed under — the same label the dépense form offers, so these
    /// rows group with the ones a practice files by hand instead of forming a category of their own.
    /// </summary>
    public const string Category = "Laboratoire";

    /// <summary>
    /// Posts the dépense if the bon owes one, and links it. Adds to the repository without saving, so the caller's
    /// own <c>SaveChangesAsync</c> commits the bon and the dépense together — a bon must never be « Reçu » with
    /// its dépense missing.
    /// </summary>
    /// <returns>The dépense that was posted, or null when none was owed.</returns>
    public static async Task<Expense?> PostIfDueAsync(
        IExpenseRepository expenses,
        LabWorkOrder order,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        if (!order.NeedsCaisseExpense)
        {
            return null;
        }

        var expense = new Expense(
            Guid.NewGuid(),
            clinicId,
            // Today's local day, as the caisse's inclusive [from, to] windows expect. `DateTime.Today` here would
            // file an evening arrival on the previous day for the whole Tunisian UTC+1 offset.
            ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday()),
            Category,
            order.Cost!.Value,
            // The bon records no mode de paiement, and inventing a prompt for one would put a dialog in front of
            // a status select. Espèces is the cabinet's default; la caisse is where it is corrected.
            PaymentMethod.Cash,
            $"Bon de prothèse — {order.WorkDescription} · {order.Prosthetist}");

        await expenses.AddAsync(expense, cancellationToken);
        order.LinkExpense(expense.Id);

        return expense;
    }
}

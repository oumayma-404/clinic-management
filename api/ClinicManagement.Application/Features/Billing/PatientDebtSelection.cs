using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing;

/// <summary>
/// Which of one patient's notes and devis carry their « Solde dû » — the input <see cref="PatientDebtLines.Project"/>
/// expects. One owner, so « Solde patient » and « Reste à payer » cannot select differently for the same patient.
/// </summary>
public static class PatientDebtSelection
{
    /// <param name="invoices">Every note of ONE patient, any status.</param>
    /// <param name="plans">Every devis of the same patient, any status.</param>
    public static (List<Invoice> LiveInvoices, List<TreatmentPlan> DebtPlans) Select(
        IEnumerable<Invoice> invoices, IEnumerable<TreatmentPlan> plans)
    {
        // Only issued, non-cancelled notes carry a balance.
        var live = invoices
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
            .ToList();

        // A devis billed into a note is counted on the note; a Draft is an unaccepted quote, not debt.
        var billedPlanIds = PlanBillingRules.BilledPlanIds(live);
        var debt = plans
            .Where(p => PlanBillingRules.CarriesDebt(p.Status) && !billedPlanIds.Contains(p.Id))
            .ToList();

        return (live, debt);
    }
}

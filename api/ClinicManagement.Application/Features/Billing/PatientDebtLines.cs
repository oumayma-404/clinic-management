using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing;

/// <summary>
/// The decomposition of « Solde patient » — one row per document that still owes.
///
/// <para><b>A pure projector over collections the caller already holds</b>, and that is the whole design.
/// <c>GetPatientBillingSummaryQueryHandler</c> loaded the patient's live invoices and their debt-bearing,
/// non-bridged plans in order to sum them, then discarded the rows and emitted scalars. This puts the rows on
/// the wire instead, from the <i>same two variables</i>, so the parts equal the whole by construction rather
/// than because two reads agree. There is no second query, no second status filter and no second copy of
/// <see cref="PlanBillingRules"/> — the filtering has already happened by the time anything here runs.</para>
///
/// <para>Pure also means the truth table is unit-testable with no repository in sight, which matters more here
/// than usual: nothing in <c>UnitTests</c> touches a database, so a rule that needed one would be a rule with
/// no test.</para>
/// </summary>
public static class PatientDebtLines
{
    /// <summary>« Kind » discriminators. Clients branch on these, never on the French label beside them.</summary>
    public const string InvoiceKind = "Invoice";

    /// <inheritdoc cref="InvoiceKind"/>
    public const string TreatmentPlanKind = "TreatmentPlan";

    /// <summary>
    /// Project the rows behind a patient's balance.
    /// </summary>
    /// <param name="liveInvoices">
    /// Already filtered by the caller to non-<c>Draft</c>, non-<c>Cancelled</c> notes — the set that carries a
    /// balance. Passing an unfiltered list would put a draft's whole TTC on the patient file as debt.
    /// </param>
    /// <param name="debtPlans">
    /// Already filtered by the caller to <c>PlanBillingRules.CarriesDebt</c> minus
    /// <c>PlanBillingRules.BilledPlanIds</c>. A bridged devis is therefore absent here, which is also why no row
    /// can offer a payment <c>RecordInstallmentPaymentCommand</c> would refuse.
    /// </param>
    /// <param name="clinicToday">
    /// From <c>ClinicClock.ClinicToday()</c>, never <c>DateTime.Today</c>. Both the note's age and the échéance's
    /// lateness are calendar-day comparisons, and Tunisia is UTC+1.
    /// </param>
    public static List<PatientDebtLineDto> Project(
        IReadOnlyCollection<Invoice> liveInvoices,
        IReadOnlyCollection<TreatmentPlan> debtPlans,
        DateTime clinicToday)
    {
        var lines = new List<PatientDebtLineDto>();

        /*
         * Which note collects an act of which devis — the continuation pairing, read straight off the marker the
         * plans already carry (`TreatmentPlanItem.BilledOnInvoiceId`). No query and no second rule: both
         * collections are already in the caller's hand, which is this projector's whole premise.
         *
         * ⚠️ **`debtPlans` only, so a cancelled or fully-settled devis pairs with nothing** — the caller has
         * already applied `CarriesDebt` and `BilledPlanIds`, and a note announcing « suite sur le devis n° X »
         * about a devis this very table does not list would send somebody looking for a row that is not there.
         */
        var planNumbersByInvoiceId = debtPlans
            .SelectMany(p => p.ActiveItems
                .Where(i => i.BilledOnInvoiceId.HasValue)
                .Select(i => (InvoiceId: i.BilledOnInvoiceId!.Value, Plan: p)))
            .GroupBy(x => x.InvoiceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => Document("du devis", x.Plan.Number)).Distinct().ToList());

        var invoiceNumbersByPlanId = debtPlans
            .ToDictionary(
                p => p.Id,
                p => p.ActiveItems
                    .Where(i => i.BilledOnInvoiceId.HasValue)
                    .Select(i => liveInvoices.FirstOrDefault(inv => inv.Id == i.BilledOnInvoiceId!.Value))
                    .Where(inv => inv is not null)
                    .Select(inv => Document("de la note", inv!.Number))
                    .Distinct()
                    .ToList());

        foreach (var invoice in liveInvoices)
        {
            if (invoice.Outstanding <= 0m)
            {
                // Settled, or over-collected — `Invoice.Outstanding` floors at 0, so this is also the
                // over-payment case. Nothing owed is nothing to show; the note lives in the Factures tab.
                continue;
            }

            lines.Add(new PatientDebtLineDto(
                Kind: InvoiceKind,
                DocumentId: invoice.Id,
                Number: invoice.Number,
                Label: "Note d'honoraires",
                Covers: Covers(invoice.Lines.Select(l => l.Designation)),
                Total: invoice.TotalTtc,
                Collected: invoice.AmountCollected,
                Outstanding: InvoiceCalculator.RoundMoney(invoice.Outstanding),
                // A legacy note with no issue date states no age rather than inventing one.
                Since: invoice.IssueDate,
                // ⚠️ Never true for a note, and the omission is the point. A note d'honoraires is payable on
                // issue, so an « en retard » derived from its own date would fire on every unpaid note the day
                // after it was raised — the exact shape that made 25 of 27 échéances read « En retard » before
                // `InstallmentLateness` existed. The row carries `Since` and lets the reader judge.
                IsOverdue: false,
                PayableInstallmentId: null,
                PayableRoom: InvoiceCalculator.RoundMoney(invoice.Outstanding),
                PartOfTreatment: Pairing(
                    planNumbersByInvoiceId.TryGetValue(invoice.Id, out var devis) ? devis : null)));
        }

        foreach (var plan in debtPlans)
        {
            if (plan.Outstanding <= 0m)
            {
                continue;
            }

            // The three plan-level facts `InstallmentLateness` needs beyond the row itself. `planIsBilled` is
            // false by construction here — a bridged plan was excluded upstream — but it is passed explicitly
            // rather than hard-coded, so this stays correct if the caller's filter ever widens.
            var planHasUnrealisedWork = plan.ActiveItems.Any(i => i.Status != TreatmentPlanItemStatus.Done);

            var unpaid = plan.Installments
                .Where(i => !i.IsPaid)
                .OrderBy(i => i.DueDate)
                .ThenBy(i => i.Id)
                .ToList();

            // What the échéancier can actually accept. Equal to the plan's outstanding in the ordinary case —
            // and deliberately computed separately, because `Installment.RecordPayment` is bounded by ONE
            // échéance's own room. When an échéancier stops summing to its plan total (`AddItems`/`RemoveItem`
            // can do it; `reconcile-money`'s `plan-schedule-balances` reports it) the two diverge, and a row
            // offering the plan's figure would produce a refusal for the number it had just printed.
            var payableRoom = InvoiceCalculator.RoundMoney(unpaid.Sum(i => i.Outstanding));

            var target = unpaid.FirstOrDefault(i => i.Outstanding > 0m);

            lines.Add(new PatientDebtLineDto(
                Kind: TreatmentPlanKind,
                DocumentId: plan.Id,
                Number: plan.Number,
                Label: "Devis",
                Covers: Covers(plan.ActiveItems.Select(i => i.DesignationFr)),
                Total: plan.TotalPlanned,
                Collected: plan.AmountPaid,
                // ⚠️ The PLAN's own figure, which is what « Solde patient » sums — never `payableRoom`. The two
                // answer different questions and the header is the one this row has to add up to.
                Outstanding: InvoiceCalculator.RoundMoney(plan.Outstanding),
                Since: target?.DueDate,
                IsOverdue: unpaid.Any(i => InstallmentLateness.IsLate(
                    i.IsPaid,
                    i.IsAutoRaised,
                    i.DueDate,
                    plan.Status,
                    planIsBilled: false,
                    planHasUnrealisedWork,
                    clinicToday)),
                PayableInstallmentId: target?.Id,
                PayableRoom: payableRoom,
                PartOfTreatment: Pairing(
                    invoiceNumbersByPlanId.TryGetValue(plan.Id, out var notes) ? notes : null)));
        }

        // Oldest debt first, and the undated last rather than first — a null `Since` is « we cannot say », not
        // « the beginning of time », and sorting it to the top would put the least-known row above the most
        // overdue one. `DocumentId` is the unique tie-break every ordered read in this solution carries.
        return lines
            .OrderBy(l => l.Since.HasValue ? 0 : 1)
            .ThenBy(l => l.Since ?? DateTime.MaxValue)
            .ThenBy(l => l.DocumentId)
            .ToList();
    }

    /// <summary>
    /// « suite de la note n° 2026-0019 », or null when this row is not half of a continuation.
    /// <para>
    /// A note that a <b>fully settled</b> devis continues never reaches here — the devis is not in
    /// <c>debtPlans</c> — which is the right silence: there is no second row to point at.
    /// </para>
    /// </summary>
    private static string? Pairing(IReadOnlyCollection<string>? counterparts) =>
        counterparts is null || counterparts.Count == 0
            ? null
            // « suite » alone — each counterpart already carries its own elided article (« du devis »,
            // « de la note »), for the reason `Document` gives.
            : $"suite {string.Join(", ", counterparts)}";

    /// <summary>
    /// « de la note n° 2026-0019 » — or « d'un devis sans numéro » / « d'une note d'honoraires » when the
    /// document has none.
    ///
    /// <para>⚠️ The article is <b>elided</b> by the caller (« du devis », « de la note ») rather than composed
    /// from « de » + « le » here, because French contracts the two and « suite de le devis » is what building
    /// it mechanically produces.</para>
    ///
    /// <para>⚠️ And through one composer rather than interpolating <c>Number</c>: a Draft note and an
    /// un-numbered followed treatment both carry null, and printing it leaves a hole in the middle of the
    /// sentence — <c>DentalRecordBillingRefusals.Document</c>'s scar, one screen over.</para>
    /// </summary>
    private static string Document(string elidedArticle, string? number) =>
        number is null
            ? (elidedArticle == "du devis" ? "d'un devis sans numéro" : "d'une note d'honoraires")
            : $"{elidedArticle} n° {number}";

    /// <summary>
    /// The work a document bills, as one short line.
    ///
    /// <para>De-duplicated and capped: an eight-act séance would otherwise render a paragraph in a table cell,
    /// and « Composite » three times over says less than « Composite » once. The remainder is counted rather
    /// than dropped silently — the full list is on the document itself, one click away.</para>
    /// </summary>
    private static string Covers(IEnumerable<string> designations)
    {
        const int maxNamed = 3;

        var named = designations
            .Select(d => d?.Trim() ?? string.Empty)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (named.Count == 0)
        {
            return string.Empty;
        }

        if (named.Count <= maxNamed)
        {
            return string.Join(", ", named);
        }

        var extra = named.Count - maxNamed;
        return string.Join(", ", named.Take(maxNamed)) + $" +{extra} autre{(extra > 1 ? "s" : "")}";
    }
}

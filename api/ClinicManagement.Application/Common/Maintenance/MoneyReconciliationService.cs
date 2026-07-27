using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Common.Maintenance;

/// <summary>How much attention a finding needs.</summary>
public enum MoneyReconciliationSeverity
{
    /// <summary>A baseline figure to record and compare against a later run. Not a problem.</summary>
    Info,

    /// <summary>Two figures that should agree do not, or data exists that should not.</summary>
    Drift
}

/// <summary>One line of the reconciliation report.</summary>
public sealed record MoneyReconciliationFinding(
    string Scope,
    string Check,
    string Detail,
    MoneyReconciliationSeverity Severity);

/// <summary>One clinic-month of collected cash, computed the way the app computes it today.</summary>
public sealed record MonthlyBaselineLine(
    string Clinic,
    int Year,
    int Month,
    decimal InvoiceCollected,
    decimal InstallmentCollected,
    decimal InstallmentCollectedLegacy)
{
    public decimal Total => InvoiceCollected + InstallmentCollected;

    /// <summary>True when the ledger attributes this month differently from the pre-ledger computation.</summary>
    public bool AttributionMoved => InstallmentCollected != InstallmentCollectedLegacy;
}

/// <summary>The full reconciliation result.</summary>
public sealed record MoneyReconciliationReport(
    IReadOnlyList<MoneyReconciliationFinding> Findings,
    IReadOnlyList<MonthlyBaselineLine> MonthlyBaseline)
{
    /// <summary>True when at least one check found a mismatch. Drives the console verb's exit code.</summary>
    public bool HasDrift => Findings.Any(f => f.Severity == MoneyReconciliationSeverity.Drift);
}

/// <summary>
/// Compares the money the app stores in two places against itself, across every clinic, and reports every
/// figure a later data migration must not move.
///
/// This is the instrument the installment-ledger and contact-sentinel migrations are verified against: run it
/// before the migration, keep the output, run it after, and diff. Every monthly « encaissé » figure must be
/// identical (spec AC-24).
///
/// Deliberately <b>not</b> DI-registered — like <see cref="AdminPasswordRecoveryService"/> it is driven only by
/// the API's <c>reconcile-money</c> console verb, so there is no HTTP-reachable path to a cross-clinic read.
/// It never mutates anything.
/// </summary>
public class MoneyReconciliationService
{
    /// <summary>Contact values written by the app in place of a real one. Both pairs must read as zero after the migration.</summary>
    private static readonly string[] SentinelEmails = { "noemail@example.com", "unknown@example.com" };
    private static readonly string[] SentinelPhones = { "0000000000", "000-000-0000" };

    private readonly IMoneyReconciliationReader _reader;

    public MoneyReconciliationService(IMoneyReconciliationReader reader)
    {
        _reader = reader;
    }

    public async Task<MoneyReconciliationReport> RunAsync(
        int monthsOfHistory = 24,
        CancellationToken cancellationToken = default)
    {
        var facts = await _reader.ReadAsync(monthsOfHistory, cancellationToken);

        var findings = new List<MoneyReconciliationFinding>();
        var baseline = new List<MonthlyBaselineLine>();

        foreach (var clinic in facts.Clinics)
        {
            findings.AddRange(CheckLedgersAgree(clinic));
            findings.AddRange(CheckPlanSchedulesBalance(clinic));
            findings.AddRange(CheckContactSentinels(clinic));
            findings.AddRange(CheckCreditNotes(clinic));
            findings.AddRange(CheckBridges(clinic));
            findings.AddRange(CheckMonthlyAttribution(clinic));
            findings.AddRange(CheckInstallmentLedgerAgrees(clinic));

            baseline.AddRange(clinic.MonthlyCollected
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .Select(m => new MonthlyBaselineLine(
                    clinic.ClinicName, m.Year, m.Month,
                    m.InvoiceCollected, m.InstallmentCollected, m.InstallmentCollectedLegacy)));
        }

        findings.AddRange(CheckOrphans(facts.Orphans));

        return new MoneyReconciliationReport(findings, baseline);
    }

    /// <summary>
    /// <c>Invoice.AmountCollected</c> is a stored column mutated only when a payment is recorded, while the caisse
    /// sums the <c>Payment</c> rows. Nothing has ever reconciled the two, so any historical drift is invisible in
    /// the app — and voiding a payment would entrench it.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckLedgersAgree(ClinicMoneyFacts clinic)
    {
        var rows = InvoiceCalculator.RoundMoney(clinic.PaymentRowSum);
        var column = InvoiceCalculator.RoundMoney(clinic.InvoiceAmountCollectedSum);

        if (rows == column)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "invoice-ledgers-agree",
                $"Σ payment rows = Σ AmountCollected = {Money(rows)}",
                MoneyReconciliationSeverity.Info);
            yield break;
        }

        yield return new MoneyReconciliationFinding(
            clinic.ClinicName,
            "invoice-ledgers-agree",
            $"Σ payment rows {Money(rows)} != Σ Invoice.AmountCollected {Money(column)} "
            + $"(difference {Money(rows - column)})",
            MoneyReconciliationSeverity.Drift);
    }

    /// <summary>
    /// « Solde patient » reads <c>TotalPlanned − Σ AmountPaid</c> while « Créances » and the dashboard read
    /// <c>Σ (Amount − AmountPaid)</c>. Those agree only while the échéancier sums to the plan total, and
    /// <c>AddItems</c>/<c>RemoveItem</c> can already break it.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckPlanSchedulesBalance(ClinicMoneyFacts clinic)
    {
        var drifted = clinic.PlanSchedules
            .Where(p => InvoiceCalculator.RoundMoney(p.InstallmentSum) != InvoiceCalculator.RoundMoney(p.TotalPlanned))
            .ToList();

        if (drifted.Count == 0)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "plan-schedule-balances",
                $"{clinic.PlanSchedules.Count} committed plan(s), all échéanciers sum to their planned total",
                MoneyReconciliationSeverity.Info);
            yield break;
        }

        foreach (var plan in drifted)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "plan-schedule-balances",
                $"Devis {plan.Number ?? plan.PlanId.ToString()[..8]}: échéancier {Money(plan.InstallmentSum)} "
                + $"!= total planifié {Money(plan.TotalPlanned)}",
                MoneyReconciliationSeverity.Drift);
        }
    }

    /// <summary>
    /// Counts the four sentinel literals, plus near-miss placeholders a clinic typed by hand. A near-miss is the
    /// dangerous one: <c>00000000</c> (eight zeros) is a <i>different string</i> that the blanking migration will
    /// not match, and it normalises to a deliverable +216 number, so the SMS gateway gets billed for it.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckContactSentinels(ClinicMoneyFacts clinic)
    {
        var sentinelEmails = clinic.ContactValues.Count(c => IsSentinelEmail(c.Email));
        var sentinelPhones = clinic.ContactValues.Count(c => IsSentinelPhone(c.Phone));
        var nearMissPhones = clinic.ContactValues.Count(c => IsNearMissPhone(c.Phone));

        var severity = sentinelEmails + sentinelPhones + nearMissPhones > 0
            ? MoneyReconciliationSeverity.Drift
            : MoneyReconciliationSeverity.Info;

        yield return new MoneyReconciliationFinding(
            clinic.ClinicName,
            "contact-sentinels",
            $"{sentinelEmails} sentinel email(s), {sentinelPhones} sentinel phone(s), "
            + $"{nearMissPhones} near-miss placeholder phone(s) out of {clinic.ContactValues.Count} patient(s)",
            severity);
    }

    private static bool IsSentinelEmail(string? email) =>
        email is not null && SentinelEmails.Contains(email.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsSentinelPhone(string? phone) =>
        phone is not null && SentinelPhones.Contains(phone.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>All-zero once separators are stripped, but not one of the literals the migration blanks.</summary>
    private static bool IsNearMissPhone(string? phone)
    {
        if (phone is null || IsSentinelPhone(phone))
        {
            return false;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && digits.All(d => d == '0');
    }

    /// <summary>
    /// The avoir amount guard is a read-then-write with no unique index, so two concurrent avoirs can both pass
    /// it and credit more than the invoice ever collected.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckCreditNotes(ClinicMoneyFacts clinic)
    {
        if (clinic.OverCreditedInvoices.Count == 0)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName, "credit-notes-within-collected", "No over-credited invoice",
                MoneyReconciliationSeverity.Info);
            yield break;
        }

        foreach (var invoice in clinic.OverCreditedInvoices)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "credit-notes-within-collected",
                $"Facture {invoice.Number ?? invoice.InvoiceId.ToString()[..8]}: avoirs {Money(invoice.Credited)} "
                + $"> encaissé {Money(invoice.AmountCollected)}",
                MoneyReconciliationSeverity.Drift);
        }
    }

    /// <summary>
    /// The devis→facture bridge refuses a second invoice with a read-then-write and no unique index, so two
    /// concurrent calls can both pass — leaving a plan billed twice.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckBridges(ClinicMoneyFacts clinic)
    {
        if (clinic.DuplicateBridges.Count == 0)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName, "one-bridge-invoice-per-plan", "No plan billed more than once",
                MoneyReconciliationSeverity.Info);
            yield break;
        }

        foreach (var bridge in clinic.DuplicateBridges)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "one-bridge-invoice-per-plan",
                $"Devis {bridge.PlanNumber ?? bridge.TreatmentPlanId.ToString()[..8]} is billed by "
                + $"{bridge.NonCancelledInvoiceCount} non-cancelled invoices",
                MoneyReconciliationSeverity.Drift);
        }
    }

    /// <summary>
    /// The installment payment ledger against the stored <c>AmountPaid</c> denormalization it derives.
    /// These are written together by the domain and backfilled together by the migration, so any difference
    /// means the ledger and the figure every read uses have diverged.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckInstallmentLedgerAgrees(ClinicMoneyFacts clinic)
    {
        var ledger = InvoiceCalculator.RoundMoney(clinic.InstallmentLedgerSum);
        var denormalized = InvoiceCalculator.RoundMoney(clinic.InstallmentAmountPaidSum);

        if (ledger == denormalized)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "installment-ledger-agrees",
                $"Σ ledger = Σ Installment.AmountPaid = {Money(ledger)}",
                MoneyReconciliationSeverity.Info);
            yield break;
        }

        yield return new MoneyReconciliationFinding(
            clinic.ClinicName,
            "installment-ledger-agrees",
            $"Σ ledger {Money(ledger)} != Σ Installment.AmountPaid {Money(denormalized)} "
            + $"(difference {Money(ledger - denormalized)})",
            MoneyReconciliationSeverity.Drift);
    }

    /// <summary>
    /// Every month whose ledger figure differs from the same month computed the old way. After the backfill
    /// this must be empty — that is spec AC-24, and it is the single check that proves the migration moved no
    /// closed month. Divergence appears only for months collected AFTER the ledger existed, where the new
    /// attribution is the correct one.
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckMonthlyAttribution(ClinicMoneyFacts clinic)
    {
        var moved = clinic.MonthlyCollected
            .Where(m => InvoiceCalculator.RoundMoney(m.InstallmentCollected)
                        != InvoiceCalculator.RoundMoney(m.InstallmentCollectedLegacy))
            .ToList();

        if (moved.Count == 0)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "monthly-attribution-unchanged",
                $"{clinic.MonthlyCollected.Count} month(s) report the same installment total both ways",
                MoneyReconciliationSeverity.Info);
            yield break;
        }

        foreach (var month in moved)
        {
            yield return new MoneyReconciliationFinding(
                clinic.ClinicName,
                "monthly-attribution-unchanged",
                $"{month.Year:0000}-{month.Month:00}: ledger {Money(month.InstallmentCollected)} "
                + $"vs ancienne méthode {Money(month.InstallmentCollectedLegacy)}",
                MoneyReconciliationSeverity.Drift);
        }
    }

    /// <summary>
    /// Invoices and treatment plans carry no foreign key to Patients, so a past cascading patient delete left
    /// them pointing at nothing while still counting toward « Créances ».
    /// </summary>
    private static IEnumerable<MoneyReconciliationFinding> CheckOrphans(OrphanFacts orphans)
    {
        var total = orphans.Invoices + orphans.TreatmentPlans + orphans.ToothStates + orphans.Notifications;

        yield return new MoneyReconciliationFinding(
            "(all clinics)",
            "no-orphaned-rows",
            $"{orphans.Invoices} invoice(s), {orphans.TreatmentPlans} treatment plan(s), "
            + $"{orphans.ToothStates} tooth state(s), {orphans.Notifications} notification(s) "
            + "pointing at a patient that no longer exists",
            total > 0 ? MoneyReconciliationSeverity.Drift : MoneyReconciliationSeverity.Info);
    }

    private static string Money(decimal value) => $"{value:0.000} DT";
}

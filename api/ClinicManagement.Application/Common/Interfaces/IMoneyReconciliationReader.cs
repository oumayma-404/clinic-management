namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Reads the raw money facts the reconciliation report compares, across <b>every</b> clinic.
///
/// Implemented in Infrastructure over the DbContext because the report deliberately spans clinics — the
/// <c>reconcile-money</c> console verb builds its container from <c>AddInfrastructure</c> only (never
/// <c>AddApplication</c>), so no <c>ICurrentClinicProvider</c> is registered and the global clinic query
/// filters are inactive. That is what makes a cross-clinic read possible without <c>IgnoreQueryFilters()</c>.
///
/// This seam exists so the comparison logic can live in Application and be unit-tested against a mocked
/// reader (mirroring <see cref="Maintenance.AdminPasswordRecoveryService"/>): the UnitTests project
/// references Application, not Infrastructure.
/// </summary>
public interface IMoneyReconciliationReader
{
    /// <param name="monthsOfHistory">How many months of « encaissé » history to include, counting back from today.</param>
    Task<MoneyReconciliationFacts> ReadAsync(int monthsOfHistory, CancellationToken cancellationToken = default);
}

/// <summary>Everything the reconciliation report needs, read in one pass.</summary>
public sealed record MoneyReconciliationFacts(
    IReadOnlyList<ClinicMoneyFacts> Clinics,
    OrphanFacts Orphans);

/// <summary>Per-clinic money facts.</summary>
public sealed record ClinicMoneyFacts(
    Guid ClinicId,
    string ClinicName,
    decimal PaymentRowSum,
    decimal InvoiceAmountCollectedSum,
    decimal InstallmentAmountPaidSum,
    decimal InstallmentLedgerSum,
    IReadOnlyList<PlanScheduleFact> PlanSchedules,
    IReadOnlyList<MonthlyCollectedFact> MonthlyCollected,
    IReadOnlyList<ContactValueFact> ContactValues,
    IReadOnlyList<OverCreditedInvoiceFact> OverCreditedInvoices,
    IReadOnlyList<DuplicateBridgeFact> DuplicateBridges,
    IReadOnlyList<UntransferredBridgeFact> UntransferredBridges);

/// <summary>One debt-bearing plan's planned total against the sum of its échéancier.</summary>
public sealed record PlanScheduleFact(Guid PlanId, string? Number, decimal TotalPlanned, decimal InstallmentSum);

/// <summary>
/// One month's collected cash, split by track.
///
/// <para>
/// <paramref name="InstallmentCollected"/> is the <b>ledger</b> figure — each payment on its own date, which is
/// what the caisse now reports. <paramref name="InstallmentCollectedLegacy"/> is the same month computed the
/// old way (the whole cumulative <c>AmountPaid</c> attributed to the single <c>LastPaidOn</c>). Both are
/// reported so a before/after run can prove the ledger migration moved no closed month (spec AC-24).
/// </para>
/// </summary>
public sealed record MonthlyCollectedFact(
    int Year,
    int Month,
    decimal InvoiceCollected,
    decimal InstallmentCollected,
    decimal InstallmentCollectedLegacy);

/// <summary>A patient's stored contact pair, used to count sentinel and near-miss placeholder values.</summary>
public sealed record ContactValueFact(string? Email, string? Phone);

/// <summary>An invoice credited by more avoirs than it ever collected.</summary>
public sealed record OverCreditedInvoiceFact(Guid InvoiceId, string? Number, decimal AmountCollected, decimal Credited);

/// <summary>
/// A bridge invoice issued BEFORE the carry-over existed, so the money collected on its devis was never moved
/// onto it — the patient is still being re-billed for a deposit they already paid.
///
/// Reported, never repaired: these are numbered fiscal documents, so the
/// correction belongs to a human with the clinic's context (an avoir, or a payment recorded by hand).
/// </summary>
public sealed record UntransferredBridgeFact(
    Guid InvoiceId,
    string? InvoiceNumber,
    string? PlanNumber,
    decimal CollectedOnPlan);

/// <summary>A treatment plan represented by more than one non-cancelled invoice.</summary>
public sealed record DuplicateBridgeFact(Guid TreatmentPlanId, string? PlanNumber, int NonCancelledInvoiceCount);

/// <summary>
/// Rows pointing at a patient that no longer exists. <c>Invoices</c> and <c>TreatmentPlans</c> have no foreign
/// key to <c>Patients</c>, so nothing at the database level has ever prevented these.
/// </summary>
public sealed record OrphanFacts(int Invoices, int TreatmentPlans, int ToothStates, int Notifications);

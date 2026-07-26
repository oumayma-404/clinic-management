using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-12a][AC-12b][AC-12c] The one shared rule behind every money read: which treatment plans carry debt,
/// and which of them are already represented by an invoice. « Solde patient », « Créances », la caisse and
/// the dashboard all route through this, so pinning it here pins all four.
/// </summary>
public class PlanBillingRulesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PlanId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherPlanId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static Invoice BridgeInvoice(Guid planId)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, treatmentPlanId: planId);
        invoice.SetLines(new[] { ("Couronne", 1, 500m) });
        return invoice;
    }

    private static Invoice IssuedBridgeInvoice(Guid planId)
    {
        var invoice = BridgeInvoice(planId);
        invoice.Issue("2026-0031", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);
        return invoice;
    }

    // [AC-12b] A Draft devis is an unaccepted quote and a Cancelled one is void — neither is debt. The three
    // committed statuses are, including Completed (all acts done ≠ paid).
    [Theory]
    [InlineData(TreatmentPlanStatus.Draft, false)]
    [InlineData(TreatmentPlanStatus.Cancelled, false)]
    [InlineData(TreatmentPlanStatus.Accepted, true)]
    [InlineData(TreatmentPlanStatus.InProgress, true)]
    [InlineData(TreatmentPlanStatus.Completed, true)]
    public void CarriesDebt_Counts_Only_Committed_Plans(TreatmentPlanStatus status, bool expected)
    {
        Assert.Equal(expected, PlanBillingRules.CarriesDebt(status));
    }

    // [AC-12c] The SQL filter used by the repository's installment aggregates must be the same set the
    // in-memory predicate answers for — otherwise « Créances » and « Solde patient » drift apart again.
    [Fact]
    public void DebtBearingPlanStatuses_Matches_CarriesDebt()
    {
        foreach (TreatmentPlanStatus status in Enum.GetValues<TreatmentPlanStatus>())
        {
            Assert.Equal(PlanBillingRules.CarriesDebt(status), PlanBillingRules.DebtBearingPlanStatuses.Contains(status));
        }
    }

    // [AC-12a] Only a real invoice replaces its plan. A Draft note isn't billed yet and a Cancelled one is
    // void, so in both cases the plan keeps carrying its own balance.
    [Theory]
    [InlineData(InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    [InlineData(InvoiceStatus.Issued, true)]
    [InlineData(InvoiceStatus.PartiallyPaid, true)]
    [InlineData(InvoiceStatus.Paid, true)]
    public void RepresentsItsPlan_Excludes_Draft_And_Cancelled(InvoiceStatus status, bool expected)
    {
        Assert.Equal(expected, PlanBillingRules.RepresentsItsPlan(status));
    }

    // [AC-12a] From loaded invoices (« Solde patient »): an issued bridge suppresses its plan.
    [Fact]
    public void BilledPlanIds_From_Invoices_Returns_The_Bridged_Plan()
    {
        var ids = PlanBillingRules.BilledPlanIds(new[] { IssuedBridgeInvoice(PlanId) });

        Assert.Equal(new[] { PlanId }, ids);
    }

    // [AC-12a] A draft bridge invoice does not yet represent the plan — the plan must stay counted, or the
    // balance would vanish between "invoice created" and "invoice issued".
    [Fact]
    public void BilledPlanIds_From_Invoices_Ignores_A_Draft_Bridge()
    {
        var ids = PlanBillingRules.BilledPlanIds(new[] { BridgeInvoice(PlanId) });

        Assert.Empty(ids);
    }

    // [AC-12a] Cancelling the bridge invoice re-opens the plan: it is no longer represented, so it returns
    // to the balance rather than disappearing from every money read.
    [Fact]
    public void BilledPlanIds_From_Invoices_Ignores_A_Cancelled_Bridge()
    {
        var invoice = IssuedBridgeInvoice(PlanId);
        invoice.Cancel("Devis modifié");

        var ids = PlanBillingRules.BilledPlanIds(new[] { invoice });

        Assert.Empty(ids);
    }

    // [AC-12a] A standalone note (no devis behind it) contributes no exclusion.
    [Fact]
    public void BilledPlanIds_From_Invoices_Ignores_A_Note_Without_A_Plan()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 60m) });
        invoice.Issue("2026-0032", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);

        Assert.Empty(PlanBillingRules.BilledPlanIds(new[] { invoice }));
    }

    // [AC-12c] The light bridge-link projection (« Créances » + dashboard) applies exactly the same status
    // rule as the loaded-invoice overload, so the clinic-wide reads and the per-patient one agree.
    [Fact]
    public void BilledPlanIds_From_Links_Applies_The_Same_Status_Rule()
    {
        var links = new List<(Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)>
        {
            (PlanId, Guid.NewGuid(), "2026-0031", InvoiceStatus.Issued),
            (OtherPlanId, Guid.NewGuid(), "2026-0030", InvoiceStatus.Cancelled)
        };

        var ids = PlanBillingRules.BilledPlanIds(links);

        Assert.Equal(new[] { PlanId }, ids);
    }
}

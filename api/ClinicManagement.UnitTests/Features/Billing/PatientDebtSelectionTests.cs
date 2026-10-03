using ClinicManagement.Application.Features.Billing;
using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Billing;

/// <summary>The one selection « Solde patient » and « Reste à payer » share: which notes and devis carry the « Solde dû ».</summary>
public class PatientDebtSelectionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Invoice Note(Guid? treatmentPlanId = null, string? number = "2026-0042")
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, treatmentPlanId: treatmentPlanId);
        invoice.SetLines(new[] { ("Couronne", 1, 1000m) });
        if (number is not null)
        {
            invoice.Issue(number);
        }
        return invoice;
    }

    private static TreatmentPlan Devis(bool accepted = true)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }) });
        if (accepted)
        {
            plan.Accept("2026-0014");
        }
        return plan;
    }

    [Fact]
    public void Only_Issued_Uncancelled_Notes_Are_Live()
    {
        var issued = Note();
        var draft = Note(number: null);
        var cancelled = Note(number: "2026-0043");
        cancelled.Cancel("Erreur de saisie");

        var (live, _) = PatientDebtSelection.Select(new[] { issued, draft, cancelled }, Array.Empty<TreatmentPlan>());

        Assert.Equal(issued.Id, Assert.Single(live).Id);
    }

    [Fact]
    public void A_Draft_Devis_Is_A_Quote_Not_Debt()
    {
        var accepted = Devis();
        var draft = Devis(accepted: false);

        var (_, debt) = PatientDebtSelection.Select(Array.Empty<Invoice>(), new[] { accepted, draft });

        Assert.Equal(accepted.Id, Assert.Single(debt).Id);
    }

    [Fact]
    public void A_Devis_Billed_On_An_Issued_Note_Is_Counted_On_The_Note()
    {
        var devis = Devis();
        var bridge = Note(devis.Id);

        var (live, debt) = PatientDebtSelection.Select(new[] { bridge }, new[] { devis });

        Assert.Single(live);
        Assert.Empty(debt);
    }

    [Fact]
    public void A_Devis_Whose_Bridge_Note_Is_Draft_Or_Cancelled_Keeps_Its_Debt()
    {
        var devis = Devis();
        var draftBridge = Note(devis.Id, number: null);
        var cancelledBridge = Note(devis.Id, number: "2026-0044");
        cancelledBridge.Cancel("Devis à revoir");

        var (live, debt) = PatientDebtSelection.Select(new[] { draftBridge, cancelledBridge }, new[] { devis });

        Assert.Empty(live);
        Assert.Equal(devis.Id, Assert.Single(debt).Id);
    }
}

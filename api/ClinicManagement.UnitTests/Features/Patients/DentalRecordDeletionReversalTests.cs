using System.Reflection;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Deleting a fiche de soins undoes the money it produced.
///
/// <para>
/// <b>The case these are written from, measured on the live database 2026-09-21.</b> One séance, three fiches
/// in nine minutes, each save collecting 80,000 DT and each deletion leaving it behind — a patient charged
/// <b>160,000 DT for an 80,000 DT extraction</b>, found by chance a week later. Every write returned 200.
/// </para>
/// </summary>
public class DentalRecordDeletionReversalTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid RecordId = Guid.NewGuid();
    private static readonly DateTime Visit = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICreditNoteRepository> _creditNotes = new();

    public DentalRecordDeletionReversalTests()
    {
        _plans.Setup(r => r.GetByCollectedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TreatmentPlan>());
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Invoice>());
        _creditNotes.Setup(r => r.GetTotalForInvoiceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
    }

    private static DentalRecord Record() => new(RecordId, PatientId, ClinicId, Visit, 0m, true);

    private Task<DentalRecordReversal> InspectAsync() => DentalRecordDeletionReversal.InspectAsync(
        _plans.Object, _invoices.Object, _creditNotes.Object, ClinicId, Record(), CancellationToken.None);

    private Task ApplyAsync(DentalRecordReversal reversal) => DentalRecordDeletionReversal.ApplyAsync(
        reversal, _plans.Object, _invoices.Object, Visit, "auth0|abc", "Dr Hamdane", CancellationToken.None);

    /// <summary>An accepted devis with one act, collected at the fiche — the shape of the reported case.</summary>
    private static TreatmentPlan PlanCollectedAtTheFiche(decimal total, decimal collected, ChequeDetails? cheque = null)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Extraction simple");
        plan.SetItems(new[] { ("Extraction simple", total, (IReadOnlyList<int>)new[] { 26 }) });
        plan.Accept("2026-0004");
        plan.CollectChairside(
            collected,
            cheque is null ? PaymentMethod.Cash : PaymentMethod.Cheque,
            Visit,
            cheque,
            RecordId);
        return plan;
    }

    private static Invoice NoteRaisedFromTheFiche(decimal amount, bool issued, bool paid)
    {
        var note = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: RecordId);
        note.SetLines(new[] { ("Extraction simple", 1, amount, (Guid?)RecordId) });
        if (issued)
        {
            note.Issue("2026-0038");
        }
        if (paid)
        {
            note.RecordPayment(amount, PaymentMethod.Cash, Visit);
        }
        return note;
    }

    // ---- The money actually comes back -------------------------------------------------------------

    [Fact]
    public async Task Collecting_At_The_Fiche_Is_Reversed_When_The_Fiche_Is_Deleted()
    {
        var plan = PlanCollectedAtTheFiche(total: 80m, collected: 80m);
        _plans.Setup(r => r.GetByCollectedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        var reversal = await InspectAsync();
        Assert.False(reversal.IsRefused);
        Assert.Equal(80m, reversal.TotalReversed);
        Assert.True(reversal.TouchesMoney);

        await ApplyAsync(reversal);

        // The ledger row is kept and marked, never erased — `AmountPaid` is derived from the live rows.
        Assert.Equal(0m, plan.AmountPaid);
        var payment = plan.Installments.SelectMany(i => i.Payments).Single();
        Assert.True(payment.IsVoided);
        Assert.Equal("Fiche de soins du 14/09/2026 supprimée", payment.VoidReason);
        Assert.Equal("Dr Hamdane", payment.VoidedByName);
    }

    [Fact]
    public async Task The_Reported_Double_Charge_Cannot_Happen_Twice()
    {
        // The whole defect in one assertion: after the reversal the devis has collected nothing, so
        // re-recording the séance on a NEW fiche starts from zero instead of adding to a payment nobody can see.
        var plan = PlanCollectedAtTheFiche(total: 80m, collected: 80m);
        _plans.Setup(r => r.GetByCollectedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        await ApplyAsync(await InspectAsync());

        Assert.Equal(0m, plan.CollectedOnRecord(RecordId));
        Assert.Equal(80m, plan.Outstanding);
    }

    [Fact]
    public async Task A_Fiche_That_Collected_Nothing_Reverses_Nothing()
    {
        var reversal = await InspectAsync();

        Assert.False(reversal.IsRefused);
        Assert.False(reversal.TouchesMoney);
        Assert.Equal(0m, reversal.TotalReversed);
        Assert.Empty(reversal.AffectedCaisseDays);
    }

    // ---- The refusals, each before anything moves --------------------------------------------------

    [Fact]
    public async Task A_Cheque_Already_Banked_Refuses_The_Deletion() // R1
    {
        var plan = PlanCollectedAtTheFiche(
            total: 80m, collected: 80m,
            cheque: ChequeDetails.For(PaymentMethod.Cheque, "123456", "BIAT", Visit.AddDays(30)));
        var installment = plan.Installments.Single();
        plan.SetInstallmentPaymentBanked(installment.Id, installment.Payments.Single().Id, banked: true);

        _plans.Setup(r => r.GetByCollectedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        var reversal = await InspectAsync();

        Assert.True(reversal.IsRefused);
        Assert.Contains("chèque", reversal.Refusal);
        // Nothing moved: the refusal is a decision, not a half-applied cascade.
        Assert.Equal(80m, plan.AmountPaid);
    }

    [Fact]
    public async Task An_Avoir_Already_Established_Refuses_The_Deletion() // R2
    {
        var note = NoteRaisedFromTheFiche(80m, issued: true, paid: true);
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { note });
        _creditNotes.Setup(r => r.GetTotalForInvoiceAsync(note.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(80m);

        var reversal = await InspectAsync();

        Assert.True(reversal.IsRefused);
        Assert.Contains("avoir", reversal.Refusal);
        Assert.Equal(80m, note.AmountCollected);
    }

    [Fact]
    public async Task A_Note_Billing_Another_Seance_Refuses_The_Deletion() // R10
    {
        var note = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: RecordId);
        note.SetLines(new[]
        {
            ("Extraction simple", 1, 80m, (Guid?)RecordId),
            ("Détartrage", 1, 120m, (Guid?)Guid.NewGuid()),
        });
        note.Issue("2026-0038");

        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { note });

        var reversal = await InspectAsync();

        Assert.True(reversal.IsRefused);
        Assert.NotEqual(InvoiceStatus.Cancelled, note.Status);
    }

    // ---- The note d'honoraires ---------------------------------------------------------------------

    [Fact]
    public async Task A_Numbered_Note_Is_Cancelled_And_Keeps_Its_Number()
    {
        var note = NoteRaisedFromTheFiche(80m, issued: true, paid: true);
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { note });

        await ApplyAsync(await InspectAsync());

        Assert.Equal(InvoiceStatus.Cancelled, note.Status);
        Assert.Equal(0m, note.AmountCollected);
        // ⚠️ The gapless sequence is the one thing that genuinely cannot be undone: the number stays.
        Assert.Equal("2026-0038", note.Number);
        _invoices.Verify(r => r.DeleteAsync(note.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Draft_Note_Is_Deleted_Rather_Than_Cancelled()
    {
        var note = NoteRaisedFromTheFiche(80m, issued: false, paid: false);
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { note });

        await ApplyAsync(await InspectAsync());

        // No number was ever spent, so there is no document to annul — `Invoice.Cancel` refuses a draft
        // outright (« un brouillon se supprime, il ne s'annule pas »).
        _invoices.Verify(r => r.DeleteAsync(note.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(note.Number);
    }

    [Fact]
    public async Task An_Already_Cancelled_Note_Is_Left_Alone()
    {
        var note = NoteRaisedFromTheFiche(80m, issued: true, paid: false);
        note.Cancel("annulée avant");
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { note });

        var reversal = await InspectAsync();

        Assert.False(reversal.IsRefused);
        Assert.Empty(reversal.Notes);
    }

    [Fact]
    public async Task A_Bridged_Note_Is_Released_From_Its_Devis_Before_It_Is_Cancelled() // R3
    {
        /*
         * The trap: `PlanBillingRules.BilledPlanIds` drops a devis from every money read while a live note
         * names it. Cancel the note without releasing the bridge and the devis' WHOLE total reappears in
         * « Solde patient » and « Créances » — while the money the note carried does not travel back, because
         * `Invoice.Cancel` says the carry is one-way. The patient would appear to owe the treatment again.
         */
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Coiffage");
        plan.SetItems(new[] { ("Coiffage pulpaire", 30m, (IReadOnlyList<int>)new[] { 27 }) });
        plan.Accept("2026-0009");

        var note = NoteRaisedFromTheFiche(30m, issued: true, paid: true);
        note.AttachToTreatmentPlan(plan.Id);
        plan.MarkItemBilledOnInvoice(plan.Items.Single().Id, note.Id, 30m);

        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { note });
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        await ApplyAsync(await InspectAsync());

        // Both halves of the bridge, not one: releasing a single side is how the link became write-once.
        Assert.Null(note.TreatmentPlanId);
        Assert.Null(plan.Items.Single().BilledOnInvoiceId);
        Assert.Equal(InvoiceStatus.Cancelled, note.Status);
    }

    // ---- The derived guard --------------------------------------------------------------------------

    /// <summary>
    /// Every FK-less pointer at a <see cref="DentalRecord"/> must be one somebody has decided about.
    ///
    /// <para>
    /// ⚠️ <b>This is the test that would have caught the reported defect years earlier.</b> The delete handler's
    /// own comment said « the two soft links to this fiche » while there were six, and the three it did not
    /// know about were the three holding money and documents. Nothing errored, because a dangling
    /// <c>Guid?</c> is not an error — it is a row nobody can reach.
    /// </para>
    /// <para>
    /// The expected set is written out <b>by hand</b>, deliberately: the point is that a human looked at a new
    /// pointer and decided what deleting a fiche should do with it, not that an expression re-derived the
    /// answer from the code it is meant to be checking. A seventh link fails this test on the day it is
    /// declared.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_Soft_Link_To_A_Fiche_Is_Accounted_For()
    {
        // entity.property -> what the deletion does with it, as decided 2026-09-21.
        var decided = new Dictionary<string, string>
        {
            // Real cascading FKs — the database clears these itself.
            ["DentalRecordAct.DentalRecordId"] = "FK CASCADE",
            ["DentalRecordTooth.DentalRecordId"] = "FK CASCADE",
            ["ToothState.DentalRecordId"] = "FK CASCADE",

            // Soft links the handler clears.
            ["TreatmentPlanItem.LinkedDentalRecordId"] = "UnmarkItemDone",
            ["TreatmentPlanItemStep.LinkedDentalRecordId"] = "UnmarkItemStep",
            ["InvoiceLine.DentalRecordId"] = "ClearDentalRecordLinks",

            // Soft links the reversal owns — the three that cost a patient 160 DT.
            ["InstallmentPayment.DentalRecordId"] = "VoidInstallmentPayment",
            ["Invoice.DentalRecordId"] = "Cancel or delete the note",
            ["MedicalDocument.DentalRecordId"] = "ReleaseFromDentalRecord — the document SURVIVES",
        };

        var found = typeof(DentalRecord).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(DentalRecord).Namespace && !t.IsAbstract)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name is "DentalRecordId" or "LinkedDentalRecordId")
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToHashSet();

        var undecided = found.Where(link => !decided.ContainsKey(link)).OrderBy(x => x).ToList();
        Assert.True(
            undecided.Count == 0,
            "A new FK-less pointer at a fiche de soins exists and nothing decides what deleting the fiche does "
            + "with it. Decide, wire it into DentalRecordDeletionReversal or the handler's detach methods, then "
            + "add it here: " + string.Join(", ", undecided));

        var stale = decided.Keys.Where(link => !found.Contains(link)).OrderBy(x => x).ToList();
        Assert.True(
            stale.Count == 0,
            "This list names a pointer that no longer exists — remove it so the guard keeps meaning something: "
            + string.Join(", ", stale));
    }
}

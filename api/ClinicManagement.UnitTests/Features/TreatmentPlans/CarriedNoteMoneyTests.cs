using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// The money of a treatment split across <b>two live documents</b> — a continuation devis and the note
/// d'honoraires that already collects its first act.
///
/// <para><b>The reported defect these exist for, and it was not an arithmetic one.</b> A 90 DT soin billed on
/// note 2026-0019 with 50 collected, continued at 10: the patient's own file correctly read « Reste à payer
/// 50,000 DT » (40 on the note + 10 on the devis), while the treatment screen said « Total convenu 10,000 ·
/// Encaissé 0,000 · Reste 10,000 » and showed the finished act at « 0,000 DT ». Every figure was true of the
/// devis and false of the treatment, and nothing on the screen said it was half of anything.</para>
///
/// <para>⚠️ <b>The invariant every case below re-asserts: `total − collected == outstanding`.</b> That is what
/// makes the three headline figures a decomposition rather than three separately-derived numbers that happen to
/// look right — the property `PatientDebtLines` earns by projecting the caller's own collections, applied here
/// to a sum across two documents.</para>
/// </summary>
public class CarriedNoteMoneyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime AsOf = new(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Seance = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    private const decimal ActCost = 90m;
    private const decimal Remaining = 10m;

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();

    public CarriedNoteMoneyTests()
    {
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        NoBridge();
        FicheBilledBy();
        Money();
    }

    /// <summary>No devis→facture bridge — the ordinary state of a continuation plan.</summary>
    private void NoBridge() =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus, decimal, decimal)>());

    /// <summary>The devis IS billed into <paramref name="note"/> — « Facturer le devis » was pressed.</summary>
    private void BridgedInto(Guid planId, Guid noteId, string number, decimal total, decimal outstanding) =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (planId, noteId, (string?)number, InvoiceStatus.Issued, total, outstanding) });

    /// <summary>Which note bills the fiche — the rows `InvoiceLinkChoice` chooses from.</summary>
    private void FicheBilledBy(params (Guid InvoiceId, string? Number, InvoiceStatus Status)[] notes) =>
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notes.Select(n => (RecordId, n.InvoiceId, n.Number, n.Status)).ToArray());

    /// <summary>The money of specific notes, by id.</summary>
    private void Money(params (Guid Id, string? Number, decimal Total, decimal Collected)[] notes) =>
        _invoices.Setup(r => r.GetMoneyByIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notes
                .Select(n => (n.Id, n.Number, InvoiceStatus.Issued, n.Total, n.Collected,
                    Math.Max(0m, n.Total - n.Collected)))
                .ToArray());

    /// <summary>The devis a continuation mints: the billed act at 0 marked against a note, the new work beside it.</summary>
    private static TreatmentPlan ContinuationPlan(Guid markedNoteId, string number = "2026-0012")
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Soin de carie / obturation");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Soin de carie / obturation", 0m, null, new List<int> { 37 }),
            new TreatmentPlanItemInput(null, "Séance suivante", Remaining, null, new List<int> { 37 }),
        });
        var first = plan.Items.OrderBy(i => i.SequenceNumber).First();
        plan.SetItemSteps(first.Id, new[] { new TreatmentPlanItemStepInput(null, "1re séance", null) });
        plan.MarkItemStepDone(first.Id, first.Steps.First().Id, Seance, RecordId);
        plan.MarkItemBilledOnInvoice(first.Id, markedNoteId, ActCost);
        plan.Accept(number);
        return plan;
    }

    private async Task<Application.DTOs.TreatmentPlanDto> Map(TreatmentPlan plan)
    {
        var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
            new[] { plan }, ClinicId, _appointments.Object, _invoices.Object, AsOf, CancellationToken.None);
        return plan.ToDto("Patient", workflow);
    }

    /// <summary>The reported case, end to end: the treatment's money is the two documents together.</summary>
    [Fact]
    public async Task The_Treatment_Money_Is_The_Devis_Plus_The_Note()
    {
        var noteId = Guid.NewGuid();
        FicheBilledBy((noteId, "2026-0019", InvoiceStatus.Issued));
        Money((noteId, "2026-0019", ActCost, 50m));

        var dto = await Map(ContinuationPlan(noteId));

        Assert.Equal(100m, dto.TreatmentTotal);      // 10 on the devis + 90 on the note
        Assert.Equal(50m, dto.TreatmentCollected);
        Assert.Equal(50m, dto.TreatmentOutstanding); // 10 still on the devis + 40 still on the note
        Assert.Equal(dto.TreatmentTotal - dto.TreatmentCollected, dto.TreatmentOutstanding);

        var carried = Assert.Single(dto.CarriedInvoices);
        Assert.Equal("2026-0019", carried.Number);
        Assert.Equal(ActCost, carried.BilledActAmount);
        Assert.False(carried.BillsOtherWork);

        // The act row can name the note instead of printing a bare 0.
        var billed = dto.Items.OrderBy(i => i.SequenceNumber).First();
        Assert.Equal(noteId, billed.BilledOnInvoiceId);
        Assert.Equal(ActCost, billed.BilledOnInvoiceAmount);
        Assert.Equal(40m, billed.BilledOnInvoiceOutstanding);
        Assert.Equal(0m, billed.PlannedCost);
    }

    /// <summary>
    /// ⚠️ <b>A CORRECTED note: the plan follows the fiche to the replacement, and does not go silent.</b>
    ///
    /// <para>Three paths cancel a note and only one is <c>CancelInvoiceCommand</c> — correcting the note and
    /// correcting its séance both cancel the old one and raise a fresh one, leaving the devis' marker naming a
    /// cancelled shell. Read off that stored id, the treatment's money would vanish from this screen and the act
    /// would go back to « 0,000 DT » the moment anybody corrected anything, while the patient's own balance
    /// stayed right — so nothing would look wrong anywhere.</para>
    /// </summary>
    [Fact]
    public async Task A_Corrected_Note_Is_Followed_To_Its_Replacement()
    {
        var cancelled = Guid.NewGuid();
        var replacement = Guid.NewGuid();

        // Both notes name the fiche; `InvoiceLinkChoice` drops the cancelled one.
        FicheBilledBy(
            (cancelled, "2026-0019", InvoiceStatus.Cancelled),
            (replacement, "2026-0020", InvoiceStatus.Issued));
        Money((replacement, "2026-0020", 95m, 50m));

        // The marker still names the ORIGINAL — nothing repointed it.
        var dto = await Map(ContinuationPlan(cancelled));

        var carried = Assert.Single(dto.CarriedInvoices);
        Assert.Equal("2026-0020", carried.Number);
        Assert.Equal(105m, dto.TreatmentTotal);       // 10 on the devis + the corrected 95
        Assert.Equal(50m, dto.TreatmentCollected);    // the payments carried onto the replacement
        Assert.Equal(55m, dto.TreatmentOutstanding);  // 10 still on the devis + 45 on the replacement
        Assert.Equal(dto.TreatmentTotal - dto.TreatmentCollected, dto.TreatmentOutstanding);
        Assert.Equal(replacement, dto.Items.OrderBy(i => i.SequenceNumber).First().BilledOnInvoiceId);
    }

    /// <summary>
    /// A note that was cancelled and never replaced carries nothing — the act falls back to printing its own
    /// cost, which is the honest reading once no document bills it. (<c>NoteCarriedActGuard</c> is what stops
    /// this state arising while the devis is live; the read must still be truthful if it does.)
    /// </summary>
    [Fact]
    public async Task A_Cancelled_Note_With_No_Replacement_Carries_Nothing()
    {
        var cancelled = Guid.NewGuid();
        FicheBilledBy((cancelled, "2026-0019", InvoiceStatus.Cancelled));
        Money();

        var dto = await Map(ContinuationPlan(cancelled));

        Assert.Empty(dto.CarriedInvoices);
        Assert.Null(dto.TreatmentTotal);
        Assert.Null(dto.TreatmentOutstanding);
        Assert.Null(dto.Items.OrderBy(i => i.SequenceNumber).First().BilledOnInvoiceId);
    }

    /// <summary>
    /// ⚠️ <b>« Facturer le devis » is offered on a continuation plan, and the rollup must read the BRIDGE's
    /// figures rather than the plan's own.</b>
    ///
    /// <para>A devis a note collects keeps an auto-raised échéance that will never see a payment, so
    /// <c>plan.Outstanding</c> reports the whole devis as unpaid for ever — measured on 4 of 4 bridged plans,
    /// two of them fully settled. Folding that raw figure in would make a dentist who bills the remaining 10 and
    /// collects it still read « Reste 50 » when the patient owes 40.</para>
    /// </summary>
    [Fact]
    public async Task Billing_The_Devis_Does_Not_Double_Count_Its_Own_Share()
    {
        var carriedNote = Guid.NewGuid();
        var bridgeNote = Guid.NewGuid();
        FicheBilledBy((carriedNote, "2026-0019", InvoiceStatus.Issued));
        Money((carriedNote, "2026-0019", ActCost, 50m));

        var plan = ContinuationPlan(carriedNote);
        // The devis' own 10 is now billed on its own note, and fully collected there.
        BridgedInto(plan.Id, bridgeNote, "2026-0021", total: Remaining, outstanding: 0m);

        var dto = await Map(plan);

        Assert.Equal(100m, dto.TreatmentTotal);       // 10 (bridge note) + 90 (carried note)
        Assert.Equal(60m, dto.TreatmentCollected);    // 10 collected on the bridge + 50 on the carried note
        Assert.Equal(40m, dto.TreatmentOutstanding);  // only the note's 40 is left
        Assert.Equal(dto.TreatmentTotal - dto.TreatmentCollected, dto.TreatmentOutstanding);
    }

    /// <summary>
    /// ⚠️ <b>A note is per-FICHE, so it may bill work this treatment has nothing to do with</b> — a détartrage
    /// done in the same séance. The share is stated (<c>BillsOtherWork</c>) rather than quietly folded into
    /// « le traitement », which is what makes the screen relabel its headline instead of overstating it. The
    /// three figures still decompose exactly.
    /// </summary>
    [Fact]
    public async Task A_Note_Billing_Other_Work_Says_So_And_Still_Adds_Up()
    {
        var noteId = Guid.NewGuid();
        FicheBilledBy((noteId, "2026-0019", InvoiceStatus.Issued));
        // 90 for the continued act + 30 of détartrage on the same séance.
        Money((noteId, "2026-0019", 120m, 50m));

        var dto = await Map(ContinuationPlan(noteId));

        var carried = Assert.Single(dto.CarriedInvoices);
        Assert.True(carried.BillsOtherWork);
        Assert.Equal(ActCost, carried.BilledActAmount);   // this treatment's share, not the note's TTC
        Assert.Equal(120m, carried.Total);
        Assert.Equal(130m, dto.TreatmentTotal);
        Assert.Equal(80m, dto.TreatmentOutstanding);
        Assert.Equal(dto.TreatmentTotal - dto.TreatmentCollected, dto.TreatmentOutstanding);
    }

    /// <summary>
    /// A stopped treatment keeps the carried act — <c>StopTreatment</c> parks only acts with <b>no delivered
    /// work</b>, and a carried act is Done by construction — so its note stays in the rollup. Parking it would
    /// erase a real debt from the one screen that explains it.
    /// </summary>
    [Fact]
    public async Task Stopping_The_Treatment_Keeps_The_Carried_Note_In_The_Rollup()
    {
        var noteId = Guid.NewGuid();
        FicheBilledBy((noteId, "2026-0019", InvoiceStatus.Issued));
        Money((noteId, "2026-0019", ActCost, 50m));

        var plan = ContinuationPlan(noteId);
        plan.StopTreatment(AsOf);

        var dto = await Map(plan);

        var carried = Assert.Single(dto.CarriedInvoices);
        Assert.Equal(ActCost, carried.Total);
        Assert.Equal(40m, dto.TreatmentOutstanding - plan.Outstanding);
    }

    /// <summary>
    /// ⚠️ <b>A DRAFT note is shown and not counted</b> — and getting this wrong would have put two money
    /// surfaces in disagreement about one patient.
    ///
    /// <para><c>GetPatientBillingSummaryQuery</c> is fed non-Draft, non-Cancelled invoices, so a draft note
    /// claims nothing: the patient's own file reads « Reste à payer 10,000 DT ». Summing the draft here would
    /// have this screen answer « Reste 100,000 DT » for the same patient at the same moment — the exact class of
    /// contradiction this feature exists to remove, re-created one screen over.</para>
    ///
    /// <para>It still appears in <c>CarriedInvoices</c>: the act sits at 0 whatever the note's status, and that
    /// 0 has to be explained.</para>
    /// </summary>
    [Fact]
    public async Task A_Draft_Carried_Note_Is_Shown_But_Never_Summed()
    {
        var noteId = Guid.NewGuid();
        FicheBilledBy((noteId, null, InvoiceStatus.Draft));
        _invoices.Setup(r => r.GetMoneyByIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (noteId, (string?)null, InvoiceStatus.Draft, ActCost, 0m, ActCost) });

        var dto = await Map(ContinuationPlan(noteId));

        // Shown — so the act row can say why it is 0.
        var carried = Assert.Single(dto.CarriedInvoices);
        Assert.Equal("Draft", carried.Status);
        Assert.Equal(noteId, dto.Items.OrderBy(i => i.SequenceNumber).First().BilledOnInvoiceId);

        // Not counted — the headline stays the devis' own, exactly as « Solde patient » reads it.
        Assert.Null(dto.TreatmentTotal);
        Assert.Null(dto.TreatmentCollected);
        Assert.Null(dto.TreatmentOutstanding);
    }

    /// <summary>
    /// ⚠️ <b>After a correction the recorded share is not quoted</b>, because it described the note it was
    /// recorded against. A séance re-priced 90 → 95 would otherwise leave the act row printing « 90,000 DT sur
    /// la note n° 2026-0020 » — a figure that appears on no document — while the note beside it says 95.
    /// </summary>
    [Fact]
    public async Task A_Replaced_Note_Does_Not_Quote_The_Old_Share()
    {
        var cancelled = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        FicheBilledBy(
            (cancelled, "2026-0019", InvoiceStatus.Cancelled),
            (replacement, "2026-0020", InvoiceStatus.Issued));
        Money((replacement, "2026-0020", 95m, 50m));

        var dto = await Map(ContinuationPlan(cancelled));

        var carried = Assert.Single(dto.CarriedInvoices);
        Assert.Equal(0m, carried.BilledActAmount);
        Assert.Equal(0m, dto.Items.OrderBy(i => i.SequenceNumber).First().BilledOnInvoiceAmount);
        // The note's own figures are still exact, which is what the screen quotes instead.
        Assert.Equal(95m, carried.Total);
        Assert.Equal(45m, carried.Outstanding);
    }

    /// <summary>An ordinary devis carries nothing, and every one of these fields stays absent.</summary>
    [Fact]
    public async Task An_Ordinary_Devis_Reports_No_Treatment_Money()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Couronne");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Couronne", 600m, null, new List<int> { 11 }) });
        plan.Accept("2026-0030");

        var dto = await Map(plan);

        Assert.Empty(dto.CarriedInvoices);
        Assert.Null(dto.TreatmentTotal);
        Assert.Null(dto.TreatmentCollected);
        Assert.Null(dto.TreatmentOutstanding);
        Assert.All(dto.Items, i => Assert.Null(i.BilledOnInvoiceId));
    }
}

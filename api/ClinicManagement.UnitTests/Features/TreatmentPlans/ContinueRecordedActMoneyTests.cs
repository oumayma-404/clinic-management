using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// Where the money goes when a recorded act is continued — the one question this command exists to answer, and
/// the one it had <b>no test for at all</b>: before this file the only match for <c>RemainingWorkCost</c> under
/// <c>UnitTests</c> was a compiled assembly.
///
/// <para>
/// The rule under test is a single sentence with two halves that must not be split: <b>a note may not be
/// attached to a plan holding money the note does not bill</b> — and « may not » is about the note's whole life,
/// not about its status now. Gating on <c>RepresentsItsPlan</c> read that status once, at continuation time, so
/// a <c>Draft</c> was attached and <i>issuing it afterwards</i> reached the forbidden state from the other end.
/// The gate is <c>remainingCost == 0</c>, which has the same answer at every moment. The bridge is all-or-nothing —
/// <see cref="PlanBillingRules.BilledPlanIds"/> drops the whole plan from every money read the moment a real note
/// names it — so a priced « travail restant » on the billed path put real money exactly where nothing looks.
/// Reported from use: a 30 DT coiffage billed on a note, continued at 10 DT, left the patient's balance at
/// <b>0</b>.
/// </para>
///
/// <para>
/// ⚠️ <b>The assertions are deliberately about the two artefacts a money read consumes</b> — the plan's
/// <c>TotalPlanned</c> and whether <c>Invoice.TreatmentPlanId</c> is set — rather than about the branch's own
/// flag. Those two are what <c>GetPatientBillingSummaryQuery</c> reads (<c>!billedPlanIds.Contains(p.Id)</c>),
/// so a refactor that keeps the flag and loses the effect fails here.
/// </para>
/// </summary>
public class ContinueRecordedActMoneyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime Intervention = new(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The reported case: « Coiffage pulpaire », 30 DT, continued for 10 DT more.</summary>
    private const decimal ActCost = 30m;
    private const decimal RemainingCost = 10m;

    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    /// <summary>The plan the handler actually persisted — every assertion below reads this.</summary>
    private TreatmentPlan? _saved;

    public ContinueRecordedActMoneyTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var patient = new Patient(PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1985, 4, 12), "Male");
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        _records.Setup(r => r.GetByIdAsync(RecordId, It.IsAny<CancellationToken>())).ReturnsAsync(Record());

        // No devis yet on this patient — the `alreadyTracked` guard must pass.
        _plans.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
                It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<TreatmentPlan>.Unpaged(Array.Empty<TreatmentPlan>()));

        _plans.Setup(r => r.GetMaxSequenceForYearAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(8);

        _plans.Setup(r => r.AddAsync(It.IsAny<TreatmentPlan>(), It.IsAny<CancellationToken>()))
            .Callback<TreatmentPlan, CancellationToken>((p, _) => _saved = p)
            .Returns((TreatmentPlan p, CancellationToken _) => Task.FromResult(p));

        // Default: nothing bills this fiche. Each billed test overrides it.
        NoNote();
    }

    private static DentalRecord Record()
    {
        var record = new DentalRecord(RecordId, PatientId, ClinicId, Intervention, ActCost, isAdultTeeth: true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(
                null, "Coiffage pulpaire", ActCost, ActCost, false, new[] { 36 },
                ToothCondition.Obturation, null, null),
        });
        return record;
    }

    private void NoNote() =>
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

    /// <summary>A note over this fiche, in <paramref name="status"/>. Returns the aggregate the handler loads.</summary>
    private Invoice NoteOverTheFiche(InvoiceStatus status)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: RecordId);
        invoice.SetLines(new[] { ("Coiffage pulpaire", 1, ActCost) });
        if (status != InvoiceStatus.Draft)
        {
            invoice.Issue("2026-0016");
        }

        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (RecordId, invoice.Id, invoice.Number, invoice.Status) });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    private ContinueRecordedActCommandHandler Handler() => new(
        _records.Object, _plans.Object, _invoices.Object, _patients.Object, _procedureTypes.Object,
        _clinicResolver.Object, _uow.Object,
        NullLogger<ContinueRecordedActCommandHandler>.Instance);

    /// <summary>
    /// The command reads the act off its own freshly loaded record, so the id passed in must be
    /// the one that record holds. This pins the handler to a single instance so the two agree.
    /// </summary>
    private Guid PinRecord()
    {
        var record = Record();
        _records.Setup(r => r.GetByIdAsync(RecordId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        return record.Acts.First().Id;
    }

    private Task<Result<Application.DTOs.TreatmentPlanDto>> ContinueActOf(Guid actId, decimal? remaining) =>
        Handler().Handle(
            new ContinueRecordedActCommand
            {
                DentalRecordId = RecordId,
                ActId = actId,
                NextStepLabel = "Suite",
                RemainingWorkCost = remaining,
            },
            CancellationToken.None);

    /// <summary>
    /// A plan that already evidences <see cref="RecordId"/> through a STEP, in <paramref name="status"/> — the
    /// shape a previous continuation leaves behind. The link is on the step, never on the act, because that is
    /// where a multi-séance act records it and the guard has to reach it there.
    /// </summary>
    private TreatmentPlan TrackingPlan(TreatmentPlanStatus status)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Coiffage pulpaire");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Coiffage pulpaire", ActCost, null, new List<int> { 36 }) });

        if (status == TreatmentPlanStatus.Draft)
        {
            plan.SetItemSteps(plan.Items.First().Id, new[]
            {
                new TreatmentPlanItemStepInput(null, "1re séance", null),
                new TreatmentPlanItemStepInput(null, "Suite", null),
            });
            MarkFirstStepAgainstTheFiche(plan);
            return plan;   // an un-numbered plan stays a Draft however much work it records
        }

        plan.Accept("2026-9999");
        plan.SetItemSteps(plan.Items.First().Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "1re séance", null),
            new TreatmentPlanItemStepInput(null, "Suite", null),
        });

        switch (status)
        {
            case TreatmentPlanStatus.Accepted:
                // Accepted with nothing recorded yet — so the fiche link goes on the act rather than a step,
                // which is the OTHER half of the guard and worth covering here rather than in a sixth case.
                plan.MarkItemDone(plan.Items.First().Id, Intervention, RecordId);
                break;
            case TreatmentPlanStatus.InProgress:
                MarkFirstStepAgainstTheFiche(plan);
                break;
            case TreatmentPlanStatus.Completed:
                MarkFirstStepAgainstTheFiche(plan);
                plan.Complete(leaveUnrealisedActs: true);
                break;
            case TreatmentPlanStatus.Cancelled:
                MarkFirstStepAgainstTheFiche(plan);
                plan.Cancel("Séance choisie par erreur");
                break;
        }

        return plan;
    }

    private void MarkFirstStepAgainstTheFiche(TreatmentPlan plan)
    {
        var item = plan.Items.First();
        plan.MarkItemStepDone(item.Id, item.Steps.First().Id, Intervention, RecordId);
    }

    /// <summary>What the patient already has on file, for the « already tracked » guard to read.</summary>
    private void PlansOnFile(params TreatmentPlan[] plans) =>
        _plans.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
                It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<TreatmentPlan>.Unpaged(plans));

    // ── the defect ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reported case. A note already collected the 30, the dentist prices 10 of new work, and the patient
    /// must still owe that 10 somewhere a money read can see it.
    ///
    /// <para>
    /// ⚠️ The two assertions are one fact: the plan carries the new work <b>and</b> the note is left unattached.
    /// Either alone is the bug — a plan carrying 10 that a note represents contributes 0 to « Solde patient »,
    /// which is exactly what shipped.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Priced_Remaining_Work_On_A_Billed_Fiche_Stays_Visible_To_Every_Money_Read()
    {
        var actId = PinRecord();
        var note = NoteOverTheFiche(InvoiceStatus.Issued);

        var result = await ContinueActOf(actId, RemainingCost);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(_saved);

        // The note is NOT attached, so `BilledPlanIds` does not drop this plan.
        Assert.Null(note.TreatmentPlanId);
        Assert.DoesNotContain(_saved!.Id, PlanBillingRules.BilledPlanIds(new[] { note }));

        // And what it carries is the new work alone — the note keeps the 30 it already billed.
        Assert.Equal(RemainingCost, _saved.TotalPlanned);
        Assert.Equal(0m, _saved.Items.OrderBy(i => i.SequenceNumber).First().PlannedCost);
        Assert.Equal(RemainingCost, _saved.Items.OrderBy(i => i.SequenceNumber).Last().PlannedCost);

        // The échéance `Accept` raises is what la caisse collects on, so it must be the 10 and not 0 or 40.
        Assert.Equal(RemainingCost, Assert.Single(_saved.Installments).Amount);
    }

    /// <summary>
    /// The devis prints a 0 on its first line, so it has to say why — and the explanation lives in the plan's
    /// notes rather than in the act's désignation, which is the act's identity on every other screen.
    /// </summary>
    [Fact]
    public async Task The_Devis_States_Which_Note_Already_Billed_The_First_Act()
    {
        var actId = PinRecord();
        NoteOverTheFiche(InvoiceStatus.Issued);

        await ContinueActOf(actId, RemainingCost);

        Assert.Contains("2026-0016", _saved!.Notes);
        Assert.Equal("Coiffage pulpaire", _saved.Items.OrderBy(i => i.SequenceNumber).First().DesignationFr);
    }

    // ── the three cases that must NOT change ────────────────────────────────────────────────────────────────

    /// <summary>
    /// No new money: the note still represents the plan and is still attached. This is the de-duplication the
    /// feature was built around — without it a 30 DT act already invoiced would be claimed by both documents.
    /// </summary>
    [Fact]
    public async Task An_Unpriced_Continuation_On_A_Billed_Fiche_Still_Attaches_The_Note()
    {
        var actId = PinRecord();
        var note = NoteOverTheFiche(InvoiceStatus.Issued);

        var result = await ContinueActOf(actId, null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(_saved!.Id, note.TreatmentPlanId);
        Assert.Equal(ActCost, _saved.TotalPlanned);
        Assert.Null(_saved.Notes);
    }

    /// <summary>
    /// ⚠️ <b>A <c>Draft</c> note is treated exactly like a live one, and the test that used to stand here
    /// asserted the opposite.</b>
    ///
    /// <para>The argument it encoded was that a Draft « represents nothing »
    /// (<see cref="PlanBillingRules.RepresentsItsPlan"/> is false), so the plan may safely carry the whole 30
    /// and pricing the first act at 0 would lose it. Every clause of that is true at the instant it is
    /// evaluated — and the conclusion was still a defect, because <b>a status read once does not stay read</b>.
    /// Reading it meant the Draft took the other branch and <i>was</i> attached; issuing the note later flips
    /// the predicate, <see cref="PlanBillingRules.BilledPlanIds"/> drops the plan whole, and the plan is holding
    /// 10 DT the note does not bill. See
    /// <see cref="Issuing_A_Draft_Note_Afterwards_Cannot_Hide_The_Remaining_Work"/>, which is the case that
    /// killed it.</para>
    ///
    /// <para>The question is therefore « does this note bill everything the plan holds », whose answer is
    /// <c>remainingCost == 0</c> at every moment of the note's life — never « does it represent the plan
    /// today ». And the 30 is not lost: it is <i>not claimed yet</i>, which is what a Draft is, and is exactly
    /// what the same fiche under the same Draft note reads with no continuation at all.</para>
    /// </summary>
    [Fact]
    public async Task A_Draft_Note_Is_Treated_Exactly_Like_A_Live_One()
    {
        var actId = PinRecord();
        var note = NoteOverTheFiche(InvoiceStatus.Draft);

        var result = await ContinueActOf(actId, RemainingCost);

        Assert.True(result.IsSuccess, result.Error);

        // The two documents are made disjoint immediately, exactly as on the live path…
        Assert.Null(note.TreatmentPlanId);
        Assert.Equal(RemainingCost, _saved!.TotalPlanned);
        Assert.Equal(0m, _saved.Items.OrderBy(i => i.SequenceNumber).First().PlannedCost);
        Assert.Equal(RemainingCost, _saved.Items.OrderBy(i => i.SequenceNumber).Last().PlannedCost);

        // …and the devis says which document holds the 30, naming a Draft as a Draft rather than printing a
        // blank where its number would be.
        Assert.Contains("brouillon de note d'honoraires", _saved.Notes);
        Assert.DoesNotContain("note n°", _saved.Notes);
    }

    /// <summary>
    /// The regression this whole gate now exists for, and the case a status-based gate structurally could not
    /// see: <b>the note is issued AFTER the continuation.</b>
    ///
    /// <para>Measured end to end before the fix — « Solde patient » read 40 DT, the note was issued, and the
    /// same read answered <b>30</b> while the plan itself still reported 40 and nothing else read it at all.
    /// Four ordinary gestures reach it (record a fiche, raise a note from <c>/factures</c> — which creates a
    /// Draft — continue the act with a priced remainder, issue the note), and one such Draft already existed on
    /// the development database.</para>
    ///
    /// <para>⚠️ Asserted through <see cref="PlanBillingRules.BilledPlanIds"/> rather than on the branch's flag,
    /// because that set is what <c>GetPatientBillingSummaryQuery</c> subtracts: a refactor that keeps the flag
    /// and loses the effect has to fail here.</para>
    /// </summary>
    [Fact]
    public async Task Issuing_A_Draft_Note_Afterwards_Cannot_Hide_The_Remaining_Work()
    {
        var actId = PinRecord();
        var note = NoteOverTheFiche(InvoiceStatus.Draft);

        var result = await ContinueActOf(actId, RemainingCost);
        Assert.True(result.IsSuccess, result.Error);

        // Nothing hides the plan while the note is a Draft — RepresentsItsPlan(Draft) is false either way, so
        // this half passed before the fix too and is here to make the *change* below unambiguous.
        Assert.DoesNotContain(_saved!.Id, PlanBillingRules.BilledPlanIds(new[] { note }));

        // ── The gesture that used to lose the money. ──
        note.Issue("2026-0016");

        Assert.True(PlanBillingRules.RepresentsItsPlan(note.Status));
        Assert.DoesNotContain(
            _saved.Id,
            PlanBillingRules.BilledPlanIds(new[] { note }));

        // The devis still owes the new work, and the note still owes its own — each counted once.
        Assert.Equal(RemainingCost, _saved.TotalPlanned);
        Assert.Equal(ActCost, note.TotalTtc);
    }

    /// <summary>
    /// The other half of the same rule, and the reason the gate is <c>remainingCost == 0</c> rather than « never
    /// attach »: with no new money the note bills <b>everything</b> the plan holds, so attaching it is a true
    /// claim and the de-duplication it buys is the point. A Draft is attached here too — it simply does not
    /// exclude anything until it is issued, at which point it correctly excludes a plan whose whole content it
    /// bills.
    /// </summary>
    [Fact]
    public async Task An_Unpriced_Continuation_On_A_DRAFT_Note_Still_Attaches_It()
    {
        var actId = PinRecord();
        var note = NoteOverTheFiche(InvoiceStatus.Draft);

        var result = await ContinueActOf(actId, null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(_saved!.Id, note.TreatmentPlanId);
        Assert.Equal(ActCost, _saved.TotalPlanned);
        Assert.Null(_saved.Notes);

        // Issued later, the note now speaks for the plan — and everything the plan holds is what it bills.
        note.Issue("2026-0016");
        Assert.Contains(_saved.Id, PlanBillingRules.BilledPlanIds(new[] { note }));
        Assert.Equal(ActCost, note.TotalTtc);
    }

    /// <summary>Never billed at all: the devis owns the whole fee, priced remainder included, and always did.</summary>
    [Fact]
    public async Task An_Unbilled_Fiche_Puts_Both_Acts_On_The_Devis()
    {
        var actId = PinRecord();

        var result = await ContinueActOf(actId, RemainingCost);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ActCost + RemainingCost, _saved!.TotalPlanned);
        Assert.Equal(2, _saved.Items.Count);
    }

    // ── a wrong continuation is recoverable ─────────────────────────────────────────

    /// <summary>
    /// Picking the wrong séance mints a numbered, accepted devis that can only be cancelled — so cancelling has
    /// to hand the fiche back, or the mistake is permanent.
    ///
    /// <para>
    /// ⚠️ <b>This is the half that had not been updated.</b> <c>GetContinuableActsQuery</c> already excluded
    /// cancelled plans, with its own note saying why; the command's guard applied no status filter, so the
    /// dialog <i>listed</i> the séance and the press <i>refused</i> it — naming a devis that no longer exists.
    /// The assertion is on the command, because the query was never the broken side.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_Fiche_Whose_Only_Devis_Was_Cancelled_Can_Be_Continued_Again()
    {
        var actId = PinRecord();
        PlansOnFile(TrackingPlan(TreatmentPlanStatus.Cancelled));

        var result = await ContinueActOf(actId, RemainingCost);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// The two near-misses. A <c>Completed</c> plan genuinely carries the work — finishing a treatment must not
    /// make its own séances continuable again — and a <c>Draft</c> is « Suivre ce traitement »'s un-numbered but
    /// clinically live plan. Only a cancelled devis releases what it held.
    /// </summary>
    [Theory]
    [InlineData(TreatmentPlanStatus.Draft)]
    [InlineData(TreatmentPlanStatus.Accepted)]
    [InlineData(TreatmentPlanStatus.InProgress)]
    [InlineData(TreatmentPlanStatus.Completed)]
    public async Task A_Live_Or_Finished_Devis_Still_Blocks_A_Second_Continuation(TreatmentPlanStatus status)
    {
        var actId = PinRecord();
        PlansOnFile(TrackingPlan(status));

        var result = await ContinueActOf(actId, RemainingCost);

        Assert.True(result.IsFailure);
        Assert.Contains("fait déjà partie d'un traitement", result.Error);
    }

    /// <summary>
    /// The list and the guard answer the same question, so a séance the dialog offers is one the press accepts.
    /// Asserted over every status rather than on the one that was wrong, because the defect was a
    /// <i>disagreement</i> and only a comparison can fail on it.
    /// </summary>
    [Theory]
    [InlineData(TreatmentPlanStatus.Draft)]
    [InlineData(TreatmentPlanStatus.Accepted)]
    [InlineData(TreatmentPlanStatus.InProgress)]
    [InlineData(TreatmentPlanStatus.Completed)]
    [InlineData(TreatmentPlanStatus.Cancelled)]
    public void What_The_List_Offers_Is_What_The_Guard_Accepts(TreatmentPlanStatus status)
    {
        var plans = new[] { TrackingPlan(status) };

        // The query filters its list against this set; the command asks the single-record question.
        var offered = !ContinuationTracking.TrackedRecordIds(plans).Contains(RecordId);
        var accepted = !ContinuationTracking.IsTracked(plans, RecordId);

        Assert.Equal(offered, accepted);
    }

    // ── the shape the booking dialog depends on ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The client books <c>schedulablePlanItems(plan)[0]</c>, and this is the server-side half of why that is the
    /// only correct reader: with a priced remainder the <b>first</b> act is finished on creation, so the séance
    /// still to book is on the second line. Taking <c>items[0]</c> booked a Done act, which the browser then
    /// could not resolve a plan for — « Le plan de traitement est requis pour lier l'acte. »
    /// </summary>
    [Fact]
    public async Task A_Priced_Remainder_Leaves_The_First_Act_Done_And_The_Second_Bookable()
    {
        var actId = PinRecord();

        await ContinueActOf(actId, RemainingCost);

        var items = _saved!.Items.OrderBy(i => i.SequenceNumber).ToList();
        Assert.Equal(TreatmentPlanItemStatus.Done, items[0].Status);
        Assert.NotEqual(TreatmentPlanItemStatus.Done, items[1].Status);
        Assert.Equal(RecordId, Assert.Single(items[0].Steps).LinkedDentalRecordId);
        Assert.Null(Assert.Single(items[1].Steps).DoneDate);
    }

    /// <summary>
    /// Without a priced remainder there is one act with two steps, the second still open — so the same reader
    /// resolves to that act, exactly as it did before any of this.
    /// </summary>
    [Fact]
    public async Task An_Unpriced_Continuation_Leaves_One_Act_With_Its_Second_Seance_Open()
    {
        var actId = PinRecord();

        await ContinueActOf(actId, null);

        var item = Assert.Single(_saved!.Items);
        Assert.NotEqual(TreatmentPlanItemStatus.Done, item.Status);
        Assert.Equal(2, item.Steps.Count);
        Assert.Single(item.Steps, s => s.DoneDate == null);
    }
}

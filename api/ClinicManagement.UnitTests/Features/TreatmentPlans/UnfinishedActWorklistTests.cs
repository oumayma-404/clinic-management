using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.TreatmentPlans.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// « Suites à planifier » — the clinic-wide list of acts a dentist marked « non terminé ».
///
/// <para>
/// ⚠️ <b>The rule that is easy to get backwards, and which these tests exist to hold: the tick says what the
/// dentist SAW, and « already being continued » is a different question with a different owner.</b> The flag is
/// never cleared automatically, so a list keyed on it alone keeps chasing work that is already on a devis — and
/// its own button would then mint a second devis over the same act, which is precisely the state
/// <c>ContinueRecordedActCommand</c> refuses on the press. The exclusion therefore goes through
/// <c>ContinuationTracking</c>, the one owner, exactly as <c>GetContinuableActsQuery</c>'s does.
/// </para>
/// <para>
/// ⚠️ <b>And the mirror rule, on the other reader: there the flag is a SORT and never a filter.</b> A fiche
/// records what was carried out and never what remains, so the tick is the only thing that knows — and the
/// easiest thing to forget. Filtering the booking dialog's list on it would turn the one gesture that helps
/// into the one gesture you cannot recover from having skipped.
/// </para>
/// </summary>
public class UnfinishedActWorklistTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OldRecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid NewRecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd02");

    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    public UnfinishedActWorklistTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var patient = new Patient(PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1985, 4, 12), "Male");
        _patients.Setup(r => r.GetByIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient> { [PatientId] = patient });

        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());

        NoPlans();
    }

    /// <summary>A fiche holding one ticked act and, optionally, one ordinary finished act beside it.</summary>
    private static DentalRecord Record(Guid id, DateTime date, bool withAFinishedActToo = false)
    {
        var record = new DentalRecord(id, PatientId, ClinicId, date, 120m, isAdultTeeth: true);
        var acts = new List<DentalRecordActInput>
        {
            new(null, "Traitement de canal", 120m, 120m, false, new[] { 36 },
                ToothCondition.Obturation, null, null, null, null, IsUnfinished: true),
        };
        if (withAFinishedActToo)
        {
            acts.Add(new DentalRecordActInput(
                null, "Détartrage", 60m, 60m, false, Array.Empty<int>(), null, null, null));
        }
        record.SetActs(acts);
        return record;
    }

    private void NoPlans() =>
        _plans.Setup(r => r.GetByLinkedDentalRecordsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TreatmentPlan>());

    private void RecordsAre(params DentalRecord[] records) =>
        _records.Setup(r => r.GetWithUnfinishedActsAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

    private Task<Result<PagedResult<ClinicManagement.Application.DTOs.UnfinishedActDto>>> Read(
        int? page = null, int? pageSize = null) =>
        new GetUnfinishedActsQueryHandler(
                _records.Object, _plans.Object, _invoices.Object, _patients.Object, _appointments.Object,
                _clinicResolver.Object, NullLogger<GetUnfinishedActsQueryHandler>.Instance)
            .Handle(new GetUnfinishedActsQuery { Page = page, PageSize = pageSize }, CancellationToken.None);

    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Only the ticked act of a mixed séance is a row — the détartrage beside it was finished.</summary>
    [Fact]
    public async Task Only_The_Ticked_Act_Of_A_Mixed_Seance_Is_A_Row()
    {
        RecordsAre(Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), withAFinishedActToo: true));

        var result = await Read();

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Traitement de canal", row.ProcedureName);
        Assert.Equal(PatientId, row.PatientId);
        Assert.Equal("Amine Trabelsi", row.PatientName);
    }

    /// <summary>
    /// ⚠️ <b>The whole point of the class.</b> The tick stays on the act for ever — it records what the dentist
    /// saw — so a fiche a devis has since picked up must leave this list by the OTHER question. Reading the flag
    /// as « still needs planning » would keep chasing booked work, and the row's own button would mint a second
    /// devis over the act.
    /// </summary>
    [Fact]
    public async Task A_Fiche_Already_Carried_By_A_Devis_Is_Not_A_Row_Even_Though_The_Tick_Remains()
    {
        var record = Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));
        RecordsAre(record);

        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Traitement de canal");
        plan.SetItems(new[] { ("Traitement de canal", 120m, (IReadOnlyList<int>)new List<int> { 36 }) });
        plan.Accept("2026-0031");
        plan.MarkItemDone(plan.Items.First().Id, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), NewRecordId);
        _plans.Setup(r => r.GetByLinkedDentalRecordsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        var result = await Read();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        // …and the act itself still says what the dentist said. Nothing cleared it.
        Assert.True(record.Acts.Single(a => a.ProcedureName == "Traitement de canal").IsUnfinished);
    }

    /// <summary>
    /// A CANCELLED devis speaks for nothing, so the act comes back — the recovery path cancelling exists to
    /// open, and the same arm <c>ContinuationTracking</c> already holds for the booking dialog's list.
    /// </summary>
    [Fact]
    public async Task A_Fiche_Whose_Only_Devis_Was_Cancelled_Is_A_Row_Again()
    {
        RecordsAre(Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)));

        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Traitement de canal");
        plan.SetItems(new[] { ("Traitement de canal", 120m, (IReadOnlyList<int>)new List<int> { 36 }) });
        plan.Accept("2026-0031");
        plan.MarkItemDone(plan.Items.First().Id, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), NewRecordId);
        plan.Cancel("Mauvaise séance choisie");
        _plans.Setup(r => r.GetByLinkedDentalRecordsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        var result = await Read();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    /// <summary>
    /// Oldest first — the opposite of <c>continuable-acts</c>, and deliberately. That list answers « which
    /// séance am I continuing? » about a patient in front of you, so the last one wins; this answers « what has
    /// been forgotten? », whose answer is the thing that has been waiting longest.
    /// </summary>
    [Fact]
    public async Task The_Longest_Waiting_Act_Is_First()
    {
        RecordsAre(
            Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)),
            Record(OldRecordId, new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc)));

        var result = await Read();

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { OldRecordId, NewRecordId },
            result.Value!.Items.Select(i => i.DentalRecordId).ToArray());
    }

    /// <summary>
    /// The money is STATED and never moved: the row names the note that already collects the act and what is
    /// still owed on it, so nobody collects it a second time on the devis the continuation will create.
    /// </summary>
    [Fact]
    public async Task A_Billed_Act_Names_Its_Note_And_What_Is_Still_Owed_On_It()
    {
        RecordsAre(Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)));

        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: NewRecordId);
        invoice.SetLines(new[] { ("Traitement de canal", 1, 120m) });
        invoice.Issue("2026-0044");
        invoice.RecordPayment(50m, PaymentMethod.Cash, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (NewRecordId, invoice.Id, invoice.Number, invoice.Status) });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var result = await Read();

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(invoice.Id, row.InvoiceId);
        Assert.Equal("2026-0044", row.InvoiceNumber);
        Assert.Equal(70m, row.InvoiceOutstanding);
    }

    /// <summary>
    /// A booking already in the diary is reported, so the row does not send somebody to make a second one.
    /// ⚠️ Per PATIENT — nothing links a booking to an act with no treatment behind it, which is what these acts
    /// are — and the DTO's own note requires the wording to match that.
    /// </summary>
    [Fact]
    public async Task A_Patient_Who_Is_Already_Coming_Back_Says_So()
    {
        RecordsAre(Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)));

        var soon = DateTime.UtcNow.AddDays(3);
        var later = DateTime.UtcNow.AddDays(10);
        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Appointment(Guid.NewGuid(), ClinicId, PatientId, null, later, TimeSpan.FromMinutes(30)),
                new Appointment(Guid.NewGuid(), ClinicId, PatientId, null, soon, TimeSpan.FromMinutes(30)),
            });

        var result = await Read();

        var row = Assert.Single(result.Value!.Items);
        // The EARLIEST one — the visit the practice will actually keep.
        Assert.Equal(soon, row.NextAppointmentAt);
    }

    /// <summary>
    /// A cancelled séance books nothing, so the row must ask again. <c>TreatmentPlanWorkflowProjection.IsLive</c>
    /// is the product's one answer to that, asked here rather than re-stated.
    /// </summary>
    [Fact]
    public async Task A_Cancelled_Booking_Does_Not_Count_As_Coming_Back()
    {
        RecordsAre(Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)));

        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, DateTime.UtcNow.AddDays(3), TimeSpan.FromMinutes(30));
        appointment.Cancel();
        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { appointment });

        var result = await Read();

        var row = Assert.Single(result.Value!.Items);
        Assert.Null(row.NextAppointmentAt);
    }

    /// <summary>
    /// The total is the whole matching set, not the page — a chip reading « 2 » that opens a list of 3 is the
    /// failure paging metadata exists to prevent.
    /// </summary>
    [Fact]
    public async Task The_Total_Counts_Every_Match_Not_The_Page()
    {
        RecordsAre(
            Record(NewRecordId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)),
            Record(OldRecordId, new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc)));

        var result = await Read(page: 1, pageSize: 1);

        Assert.Single(result.Value!.Items);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(OldRecordId, result.Value.Items[0].DentalRecordId);
    }

    /// <summary>A read that matches nothing is an empty page, never a failure.</summary>
    [Fact]
    public async Task Nothing_Ticked_Is_An_Empty_Page()
    {
        RecordsAre();

        var result = await Read();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }
}

/// <summary>
/// The tick has to survive the round trip through the fiche's own editor, and the ways it can silently fail to.
/// </summary>
/// <remarks>
/// ⚠️ <c>DentalRecord.SetActs</c> replaces the whole act list on every save, so an act rebuilt without
/// <c>IsUnfinished</c> is marked finished by an ordinary re-save — no gesture, no toast, and the act simply
/// leaves « Suites à planifier ». That is the trap <c>PonticToothNumbers</c> and <c>MinDaysAfterPrevious</c>
/// already paid for, one field over, and it is why the read-back is pinned here rather than trusted.
/// </remarks>
public class UnfinishedActRoundTripTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static DentalRecord RecordWithTickedAct()
    {
        var record = new DentalRecord(
            Guid.NewGuid(), PatientId, ClinicId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            120m, isAdultTeeth: true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(
                null, "Traitement de canal", 120m, 120m, false, new[] { 36 },
                ToothCondition.Obturation, null, null, null, null, IsUnfinished: true),
        });
        return record;
    }

    /// <summary>The aggregate stores what the input said.</summary>
    [Fact]
    public void The_Aggregate_Keeps_The_Tick()
    {
        Assert.True(RecordWithTickedAct().Acts.Single().IsUnfinished);
    }

    /// <summary>An act nobody ticked is finished — the default, and what every row written before this means.</summary>
    [Fact]
    public void An_Act_Nobody_Ticked_Is_Finished()
    {
        var record = new DentalRecord(
            Guid.NewGuid(), PatientId, ClinicId, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            60m, isAdultTeeth: true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(null, "Détartrage", 60m, 60m, false, Array.Empty<int>(), null, null, null),
        });

        Assert.False(record.Acts.Single().IsUnfinished);
    }

    /// <summary>
    /// ⚠️ The read-back the editor depends on. Without it the fiche reopens with the box unticked and the next
    /// save erases the dentist's own statement.
    /// </summary>
    [Fact]
    public void The_Dto_Carries_The_Tick_Back_To_The_Editor()
    {
        Assert.True(RecordWithTickedAct().ToDto().Acts.Single().IsUnfinished);
    }

    /// <summary>
    /// And the wire input carries it forward again, so reopening a fiche and pressing « Enregistrer » with
    /// nothing changed leaves the act exactly as it was. This is the full loop the trap breaks.
    /// </summary>
    [Fact]
    public void Re_Saving_An_Untouched_Fiche_Preserves_The_Tick()
    {
        var dto = RecordWithTickedAct().ToDto();

        var parsed = DentalRecordActParser.Parse(dto.Acts
            .Select(a => new ClinicManagement.Application.DTOs.DentalActInput
            {
                ProcedureTypeId = a.ProcedureTypeId,
                ProcedureName = a.ProcedureName,
                Cost = a.Cost,
                UnitCost = a.UnitCost,
                IsPerTooth = a.IsPerTooth,
                ToothNumbers = a.ToothNumbers,
                PonticToothNumbers = a.PonticToothNumbers,
                ImplantPilierToothNumbers = a.ImplantPilierToothNumbers,
                IsUnfinished = a.IsUnfinished,
                ResultingCondition = a.ResultingCondition,
                Surfaces = a.Surfaces,
                Note = a.Note,
            })
            .ToList());

        Assert.True(parsed.IsSuccess);
        Assert.True(parsed.Value!.Single().IsUnfinished);
    }

    /// <summary>
    /// ⚠️ A <c>with</c> expression preserves it — which is what makes <c>PlanCarriedActPricing</c> safe, and is
    /// the shape any future copy site must use. Re-listing the positional arguments is what drops the field.
    /// </summary>
    [Fact]
    public void Copying_The_Input_With_A_With_Expression_Preserves_The_Tick()
    {
        var input = new DentalRecordActInput(
            null, "Traitement de canal", 120m, 120m, false, new[] { 36 },
            ToothCondition.Obturation, null, null, null, null, IsUnfinished: true);

        var repriced = input with { Cost = 0m, UnitCost = 0m };

        Assert.True(repriced.IsUnfinished);
    }
}

/// <summary>
/// The other half of the rule, on the reader that has to behave in the opposite way.
/// </summary>
/// <remarks>
/// ⚠️ <b>In « C'est la suite d'une séance précédente ? » the tick is a SORT and must never become a filter.</b>
/// A fiche records what was carried out and never what remains, so the tick is the only thing in the product
/// that knows an act was left unfinished — and it is also the easiest thing to forget, since it is one checkbox
/// on a form filled in at the end of a séance. Filtering this list on it would turn the one gesture that helps
/// into the one gesture you cannot recover from having skipped: the dentist who forgot would find the act
/// simply absent, with nothing to say why. So the ticked acts rise to the top and every other recent act stays
/// exactly where it was.
/// </remarks>
public class ContinuableActOrderingTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    public ContinuableActOrderingTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1985, 4, 12), "Male"));
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
        _plans.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
                It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<TreatmentPlan>.Unpaged(Array.Empty<TreatmentPlan>()));
    }

    /// <summary>One fiche, one act, dated relative to now so the 120-day window always covers it.</summary>
    private static DentalRecord Record(string name, int daysAgo, bool ticked)
    {
        var record = new DentalRecord(
            Guid.NewGuid(), PatientId, ClinicId, DateTime.UtcNow.AddDays(-daysAgo), 100m, isAdultTeeth: true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(
                null, name, 100m, 100m, false, new[] { 36 }, ToothCondition.Obturation, null, null,
                null, null, IsUnfinished: ticked),
        });
        return record;
    }

    private Task<Result<List<ClinicManagement.Application.DTOs.ContinuableActDto>>> Read(
        params DentalRecord[] records)
    {
        _records.Setup(r => r.GetByPatientIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);
        return new GetContinuableActsQueryHandler(
                _records.Object, _plans.Object, _invoices.Object, _patients.Object, _clinicResolver.Object,
                NullLogger<GetContinuableActsQueryHandler>.Instance)
            .Handle(new GetContinuableActsQuery { PatientId = PatientId }, CancellationToken.None);
    }

    /// <summary>The ticked act comes first even though an unticked séance is more recent.</summary>
    [Fact]
    public async Task A_Ticked_Act_Outranks_A_More_Recent_Unticked_One()
    {
        var result = await Read(
            Record("Détartrage", daysAgo: 2, ticked: false),
            Record("Traitement de canal", daysAgo: 30, ticked: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "Traitement de canal", "Détartrage" },
            result.Value!.Select(a => a.ProcedureName).ToArray());
        Assert.True(result.Value[0].IsUnfinished);
    }

    /// <summary>
    /// ⚠️ <b>And nothing is removed.</b> The forgotten tick is the case this list exists to survive — see the
    /// class remark. An act that nobody marked is still offered, just not first.
    /// </summary>
    [Fact]
    public async Task An_Act_Nobody_Ticked_Is_Still_Offered()
    {
        var result = await Read(
            Record("Détartrage", daysAgo: 2, ticked: false),
            Record("Traitement de canal", daysAgo: 30, ticked: true));

        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, a => a.ProcedureName == "Détartrage" && !a.IsUnfinished);
    }

    /// <summary>With nothing ticked the list is unchanged: most recent first, exactly as before.</summary>
    [Fact]
    public async Task With_No_Tick_Anywhere_The_Order_Is_Still_Most_Recent_First()
    {
        var result = await Read(
            Record("Détartrage", daysAgo: 2, ticked: false),
            Record("Extraction simple", daysAgo: 30, ticked: false));

        Assert.Equal(
            new[] { "Détartrage", "Extraction simple" },
            result.Value!.Select(a => a.ProcedureName).ToArray());
    }
}

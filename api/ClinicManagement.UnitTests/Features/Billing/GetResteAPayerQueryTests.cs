using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Billing;

/// <summary>« Reste à payer »: who owes, split « À relancer » / « En cours ». Mocks mirror the real repository filters.</summary>
// Dates are decades away because the handler reads the real clock.
public class GetResteAPayerQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AmelId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid BilelId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid ChaimaId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    private static readonly DateTime LongAgo = new(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FarAhead = new(2099, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NextVisit = new(2098, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private readonly List<Patient> _patientRows = new();
    private readonly List<Invoice> _invoiceRows = new();
    private readonly List<TreatmentPlan> _planRows = new();
    private readonly Dictionary<Guid, DateTime> _nextVisits = new();

    public GetResteAPayerQueryTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>)_invoiceRows
                .Where(i => i.TreatmentPlanId.HasValue)
                .Select(i => (i.TreatmentPlanId!.Value, i.Id, i.Number, i.Status, i.TotalTtc, i.Outstanding))
                .ToList());
        _invoices.Setup(r => r.GetOutstandingByPatientAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<(Guid, decimal, DateTime?)>)_invoiceRows
                .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
                .GroupBy(i => i.PatientId)
                .Select(g => (g.Key, g.Sum(i => i.Outstanding), g.Min(i => i.IssueDate)))
                .Where(r => r.Item2 > 0m)
                .ToList());
        _plans.Setup(r => r.GetPatientIdsWithPlanOutstandingAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> excluded, CancellationToken _) =>
                (IReadOnlyList<Guid>)_planRows
                    .Where(p => PlanBillingRules.CarriesDebt(p.Status) && !excluded.Contains(p.Id)
                                && p.TotalPlanned > p.Installments.Sum(i => i.AmountPaid))
                    .Select(p => p.PatientId)
                    .Distinct()
                    .ToList());
        _invoices.Setup(r => r.GetByPatientIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyList<Invoice>)_invoiceRows.Where(i => ids.Contains(i.PatientId)).ToList());
        _plans.Setup(r => r.GetByPatientIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyList<TreatmentPlan>)_planRows.Where(p => ids.Contains(p.PatientId)).ToList());
        _patients.Setup(r => r.GetByIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, Patient>)_patientRows.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id));
        _appointments.Setup(r => r.GetNextBookingByPatientAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, DateTime _, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, DateTime>)_nextVisits.Where(v => ids.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value));
    }

    private void AddPatient(Guid id, string firstName, string lastName, string? phone = null) =>
        _patientRows.Add(new Patient(
            id, ClinicId, firstName, lastName, new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "F",
            phoneNumber: phone is null ? null : new PhoneNumber(phone)));

    private Invoice AddNote(Guid patientId, decimal total, string number)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, patientId);
        invoice.SetLines(new[] { ("Détartrage", 1, total) });
        invoice.Issue(number);
        _invoiceRows.Add(invoice);
        return invoice;
    }

    private TreatmentPlan AddDevis(Guid patientId, decimal total, string? number, params (DateTime dueDate, decimal amount)[] schedule)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, patientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", total, (IReadOnlyList<int>)new[] { 11 }) });
        if (schedule.Length > 0)
        {
            plan.SetInstallments(schedule);
        }
        if (number is not null)
        {
            plan.Accept(number);
        }
        _planRows.Add(plan);
        return plan;
    }

    /// <summary>Amel: a 450 note + a Draft devis · Bilel: a devis 400 late / 600 to come · Chaima: unfinished work, a séance booked.</summary>
    private void ArrangeThreePatients()
    {
        AddPatient(AmelId, "Amel", "Ben Ali", "+21620123456");
        AddNote(AmelId, 450m, "2026-0042");
        AddDevis(AmelId, 800m, number: null);

        AddPatient(BilelId, "Bilel", "Trabelsi");
        AddDevis(BilelId, 1000m, "2026-0014", (LongAgo, 400m), (FarAhead, 600m));

        AddPatient(ChaimaId, "Chaima", "Gharbi");
        AddDevis(ChaimaId, 300m, "2026-0015");
        _nextVisits[ChaimaId] = NextVisit;
    }

    private async Task<ResteAPayerPageDto> RunAsync(
        ResteAPayerList list,
        ResteAPayerSort sort = ResteAPayerSort.Age,
        string? search = null,
        int? page = null,
        int? pageSize = null)
    {
        var handler = new GetResteAPayerQueryHandler(
            _invoices.Object, _plans.Object, _patients.Object, _appointments.Object, _clinicResolver.Object,
            NullLogger<GetResteAPayerQueryHandler>.Instance);
        var result = await handler.Handle(
            new GetResteAPayerQuery { List = list, Sort = sort, SearchTerm = search, Page = page, PageSize = pageSize },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static Guid[] Ids(ResteAPayerPageDto page) => page.Items.Select(r => r.PatientId).ToArray();

    [Fact]
    public async Task A_Devis_With_A_Late_And_A_Future_Echeance_Puts_Its_Patient_In_Both_Lists()
    {
        ArrangeThreePatients();

        var due = await RunAsync(ResteAPayerList.Due);
        var running = await RunAsync(ResteAPayerList.Running);

        Assert.Equal(new[] { AmelId, BilelId }.OrderBy(x => x), Ids(due).OrderBy(x => x));
        Assert.Equal(new[] { BilelId, ChaimaId }.OrderBy(x => x), Ids(running).OrderBy(x => x));
        var bilel = Assert.Single(due.Items, r => r.PatientId == BilelId);
        Assert.Equal(400m, bilel.DueAmount);
        Assert.Equal(600m, bilel.RunningAmount);
    }

    [Fact]
    public async Task A_Draft_Treatment_Adds_Nothing_To_Either_List()
    {
        ArrangeThreePatients();

        var amel = Assert.Single((await RunAsync(ResteAPayerList.Due)).Items, r => r.PatientId == AmelId);

        Assert.Equal(450m, amel.DueAmount);
        Assert.Equal(0m, amel.RunningAmount);
        Assert.DoesNotContain(AmelId, Ids(await RunAsync(ResteAPayerList.Running)));
    }

    [Fact]
    public async Task Both_Cards_Count_Every_Matching_Patient_Not_One_Page()
    {
        ArrangeThreePatients();

        var page = await RunAsync(ResteAPayerList.Due, pageSize: 1);

        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(850m, page.DueTotal);
        Assert.Equal(2, page.DueCount);
        Assert.Equal(900m, page.RunningTotal);
        Assert.Equal(2, page.RunningCount);
    }

    [Fact]
    public async Task Search_Narrows_The_List_And_Both_Cards_Together()
    {
        ArrangeThreePatients();

        var page = await RunAsync(ResteAPayerList.Due, search: "trabelsi");

        Assert.Equal(new[] { BilelId }, Ids(page));
        Assert.Equal(400m, page.DueTotal);
        Assert.Equal(1, page.DueCount);
        Assert.Equal(600m, page.RunningTotal);
        Assert.Equal(1, page.RunningCount);
    }

    [Fact]
    public async Task The_Due_List_Is_Oldest_Debt_First_By_Default()
    {
        ArrangeThreePatients();

        Assert.Equal(new[] { BilelId, AmelId }, Ids(await RunAsync(ResteAPayerList.Due)));
    }

    [Fact]
    public async Task The_Running_List_Is_Next_Seance_Or_Echeance_First_By_Default()
    {
        ArrangeThreePatients();

        Assert.Equal(new[] { ChaimaId, BilelId }, Ids(await RunAsync(ResteAPayerList.Running)));
    }

    [Fact]
    public async Task Both_Lists_Sort_By_Their_Own_Amount_Largest_First()
    {
        ArrangeThreePatients();

        Assert.Equal(new[] { AmelId, BilelId }, Ids(await RunAsync(ResteAPayerList.Due, ResteAPayerSort.Amount)));
        Assert.Equal(new[] { BilelId, ChaimaId }, Ids(await RunAsync(ResteAPayerList.Running, ResteAPayerSort.Amount)));
    }

    [Fact]
    public async Task Equal_Amounts_And_Names_End_On_The_Patient_Id()
    {
        var first = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("20000000-0000-0000-0000-000000000002");
        AddPatient(second, "Sami", "Ben Salah");
        AddPatient(first, "Sami", "Ben Salah");
        AddNote(second, 100m, "2026-0070");
        AddNote(first, 100m, "2026-0071");

        Assert.Equal(new[] { first, second }, Ids(await RunAsync(ResteAPayerList.Due, ResteAPayerSort.Amount)));
    }

    [Fact]
    public async Task A_Page_Past_The_End_Is_Empty_And_Keeps_Both_Totals()
    {
        ArrangeThreePatients();

        var page = await RunAsync(ResteAPayerList.Due, page: 3, pageSize: 25);

        Assert.Empty(page.Items);
        Assert.Equal(850m, page.DueTotal);
        Assert.Equal(2, page.DueCount);
    }

    [Fact]
    public async Task A_Row_Names_Its_Reason_Document_Age_And_What_Comes_Next()
    {
        ArrangeThreePatients();
        var daysBefore = (ClinicClock.ClinicToday() - LongAgo.Date).Days;

        var bilel = Assert.Single((await RunAsync(ResteAPayerList.Due)).Items, r => r.PatientId == BilelId);

        Assert.Equal("Échéance du 15/01 dépassée", bilel.DueReason);
        Assert.Equal("Devis n° 2026-0014 · Couronne", bilel.DueDocument);
        Assert.Equal(1, bilel.DueDocumentCount);
        Assert.Equal(LongAgo, bilel.DueSince);
        // The handler reads the clock after this test did, so midnight may pass between the two.
        Assert.InRange(bilel.DueDays!.Value, daysBefore, daysBefore + 1);
        Assert.Equal("0 acte fait sur 1", bilel.RunningReason);
        Assert.Equal(0, bilel.RunningActsDone);
        Assert.Equal(1, bilel.RunningActsTotal);
        Assert.Equal(FarAhead, bilel.NextInstallmentDue);
        Assert.Null(bilel.NextVisit);
    }

    [Fact]
    public async Task A_Note_Row_Names_The_Note_And_Its_Acts()
    {
        ArrangeThreePatients();

        var amel = Assert.Single((await RunAsync(ResteAPayerList.Due)).Items, r => r.PatientId == AmelId);

        Assert.StartsWith("Note du ", amel.DueReason);
        Assert.Equal("Note n° 2026-0042 · Détartrage", amel.DueDocument);
        Assert.Null(amel.RunningReason);
        Assert.Null(amel.RunningActsTotal);
    }

    [Fact]
    public async Task A_Row_Carries_The_Stored_Dialable_Number_Or_None()
    {
        ArrangeThreePatients();

        var due = await RunAsync(ResteAPayerList.Due);

        Assert.Equal("+21620123456", Assert.Single(due.Items, r => r.PatientId == AmelId).PhoneE164);
        Assert.Null(Assert.Single(due.Items, r => r.PatientId == BilelId).PhoneE164);
    }

    [Fact]
    public async Task A_Partial_Payment_Leaves_The_Remainder_On_The_Row_And_The_Card()
    {
        ArrangeThreePatients();
        _invoiceRows.Single(i => i.PatientId == AmelId).RecordPayment(200m, PaymentMethod.Cash, DateTime.UtcNow);

        var page = await RunAsync(ResteAPayerList.Due);

        Assert.Equal(250m, Assert.Single(page.Items, r => r.PatientId == AmelId).DueAmount);
        Assert.Equal(650m, page.DueTotal);
    }

    [Fact]
    public async Task A_Settled_Note_Takes_Its_Patient_Off_The_List()
    {
        ArrangeThreePatients();
        var note = _invoiceRows.Single(i => i.PatientId == AmelId);
        note.RecordPayment(note.TotalTtc, PaymentMethod.Cash, DateTime.UtcNow);

        var page = await RunAsync(ResteAPayerList.Due);

        Assert.Equal(new[] { BilelId }, Ids(page));
        Assert.Equal(1, page.DueCount);
    }

    [Fact]
    public async Task A_Devis_Billed_On_A_Note_Counts_Once_On_The_Note()
    {
        AddPatient(AmelId, "Amel", "Ben Ali");
        var devis = AddDevis(AmelId, 1000m, "2026-0014");
        var bridge = new Invoice(Guid.NewGuid(), ClinicId, AmelId, treatmentPlanId: devis.Id);
        bridge.SetLines(new[] { ("Couronne", 1, 1000m) });
        bridge.Issue("2026-0031");
        _invoiceRows.Add(bridge);

        var page = await RunAsync(ResteAPayerList.Due);

        var amel = Assert.Single(page.Items);
        Assert.Equal(1000m, amel.DueAmount);
        Assert.Equal(0m, amel.RunningAmount);
        Assert.Equal(0, page.RunningCount);
    }

    [Fact]
    public async Task A_Devis_Owing_An_Act_Added_After_Every_Echeance_Was_Paid_Is_Still_Listed()
    {
        AddPatient(BilelId, "Bilel", "Trabelsi");
        var devis = AddDevis(BilelId, 1000m, "2026-0014", (FarAhead, 1000m));
        devis.RecordInstallmentPayment(devis.Installments.Single().Id, 1000m, PaymentMethod.Cash, DateTime.UtcNow);
        devis.AddItems(new[] { ("Greffe osseuse", 200m, (IReadOnlyList<int>)new[] { 12 }) });

        var page = await RunAsync(ResteAPayerList.Running);

        var bilel = Assert.Single(page.Items);
        Assert.Equal(200m, bilel.RunningAmount);
        Assert.Equal(200m, page.RunningTotal);
    }

    [Fact]
    public async Task An_Unresolved_Clinic_Fails()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Cabinet introuvable."));
        var handler = new GetResteAPayerQueryHandler(
            _invoices.Object, _plans.Object, _patients.Object, _appointments.Object, _clinicResolver.Object,
            NullLogger<GetResteAPayerQueryHandler>.Instance);

        var result = await handler.Handle(new GetResteAPayerQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}

using ClinicManagement.UnitTests.Common;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Billing;

/// <summary>
/// [J7] « Créances » can age <b>invoice</b> debt, which is where most of a clinic's debt is.
///
/// <para>
/// <c>GetOutstandingByPatientAsync</c> returned <c>(PatientId, Outstanding)</c> with <b>no date</b>, so
/// <c>oldestOverdue</c> was populated only inside the plan loop and the « Retard » column was blank for any
/// patient whose debt was a plain unpaid note d'honoraires. An échéancier is what a patient asked to pay in
/// instalments; an unpaid note is the ordinary way a bill goes unpaid — so the column was empty for the
/// common case and filled for the rarer one.
/// </para>
/// <para>
/// A note has no due date: it is payable on issue, so its issue date <i>is</i> when the debt started. Where a
/// patient carries both kinds, « Retard » is the <b>earlier</b> of the two — a six-month-old note beside a
/// week-late échéance means the patient has been owing for six months.
/// </para>
/// </summary>
public class InvoiceDebtIsAgedTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    public InvoiceDebtIsAgedTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var patient = new Patient(
            PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));
        _patients.Setup(r => r.GetByIdsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<Guid, Patient>)new Dictionary<Guid, Patient> { [PatientId] = patient });

        // Defaults: no debt on either track. Each test opts into the one(s) it is about.
        _invoices.Setup(r => r.GetOutstandingByPatientAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, decimal, DateTime?)>());
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
        _plans.Setup(r => r.GetInstallmentOutstandingByPatientAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, decimal, DateTime?)>());
    }

    private void InvoiceDebt(decimal amount, DateTime? oldestUnpaidIssueDate)
    {
        _invoices.Setup(r => r.GetOutstandingByPatientAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (PatientId, amount, oldestUnpaidIssueDate) });
    }

    private void PlanDebt(decimal amount, DateTime? oldestOverdueDueDate)
    {
        _plans.Setup(r => r.GetInstallmentOutstandingByPatientAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (PatientId, amount, oldestOverdueDueDate) });
    }

    private async Task<ReceivableDto> RowAsync()
    {
        var handler = new GetReceivablesQueryHandler(
            _invoices.Object, _plans.Object, _patients.Object, _clinicResolver.Object,
            NullLogger<GetReceivablesQueryHandler>.Instance);

        var result = await handler.Handle(new GetReceivablesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return Assert.Single(result.Value!.Items);
    }

    /// <summary>N clinic-local days before today, as the instant an issue date would hold.</summary>
    private static DateTime DaysAgo(int days) =>
        ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday().AddDays(-days));

    // [J7] The defect, pinned: pure invoice debt is now aged. This assertion was impossible before — the read
    // returned no date, so `DaysOverdue` was structurally always null on this branch.
    [Fact]
    public async Task Pure_Invoice_Debt_Reports_Its_Age()
    {
        InvoiceDebt(1200m, DaysAgo(45));

        var row = await RowAsync();

        Assert.Equal(1200m, row.TotalOutstanding);
        Assert.Equal(45, row.DaysOverdue);
        Assert.NotNull(row.OldestOverdueDate);
    }

    // [J7] Where a patient owes on both tracks, « Retard » is the EARLIER date. Taking the plan's (as the old
    // code structurally did, by overwriting) would under-report a much older note.
    [Fact]
    public async Task The_Earlier_Of_The_Two_Tracks_Wins()
    {
        InvoiceDebt(1000m, DaysAgo(180));   // a six-month-old note
        PlanDebt(500m, DaysAgo(7));         // an échéance a week late

        var row = await RowAsync();

        Assert.Equal(1500m, row.TotalOutstanding);
        Assert.Equal(180, row.DaysOverdue);
    }

    // [J7] …and symmetrically, an older échéance beats a recent note. The rule is "earliest", not "invoice wins".
    [Fact]
    public async Task An_Older_Installment_Still_Wins()
    {
        InvoiceDebt(1000m, DaysAgo(3));
        PlanDebt(500m, DaysAgo(90));

        var row = await RowAsync();

        Assert.Equal(90, row.DaysOverdue);
    }

    // [J7] A note issued TODAY is debt with an age of zero, not « en retard ». The frontend renders the badge only
    // when `daysOverdue > 0`, so 0 must be reported honestly rather than clamped up to 1 or left null.
    [Fact]
    public async Task A_Note_Issued_Today_Is_Zero_Days_Not_Overdue()
    {
        InvoiceDebt(300m, DaysAgo(0));

        var row = await RowAsync();

        Assert.Equal(0, row.DaysOverdue);
    }

    // [J7] A legacy row with no issue date at all yields null — « we do not know since when », which is a
    // different claim from « zero days » and must not be rendered as one.
    [Fact]
    public async Task A_Null_Issue_Date_Reports_No_Age()
    {
        InvoiceDebt(300m, null);

        var row = await RowAsync();

        Assert.Equal(300m, row.TotalOutstanding);
        Assert.Null(row.DaysOverdue);
        Assert.Null(row.OldestOverdueDate);
    }

    // [J7] A plan whose échéances are all still in the future has debt but no age — the plan side reports null and
    // the invoice side contributes nothing, so the row must not invent a date from the other track's absence.
    [Fact]
    public async Task Future_Only_Installment_Debt_Reports_No_Age()
    {
        PlanDebt(500m, null);

        var row = await RowAsync();

        Assert.Equal(500m, row.TotalOutstanding);
        Assert.Null(row.DaysOverdue);
    }

    // [J7] The count is in CLINIC days (AC-P6.4). Tunisia is UTC+1, so a note issued at 23:30 UTC belongs to the
    // *next* clinic day — counting in UTC days would report one day too many for every note issued after 23:00.
    [Fact]
    public async Task The_Age_Is_Counted_In_Clinic_Days()
    {
        // 23:30 UTC yesterday == 00:30 today in Tunis, i.e. TODAY for the clinic: zero days old.
        var lateLastNightUtc = ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday()).AddMinutes(30);
        InvoiceDebt(300m, lateLastNightUtc);

        var row = await RowAsync();

        Assert.Equal(0, row.DaysOverdue);
    }

    // [J7] Negative ages are impossible: a future-dated note reads as 0, never as « -5 jours ».
    [Fact]
    public async Task A_Future_Issue_Date_Never_Reports_A_Negative_Age()
    {
        InvoiceDebt(300m, DaysAgo(-5));

        var row = await RowAsync();

        Assert.Equal(0, row.DaysOverdue);
    }

    // [J7] The total is still the sum of both tracks — the aging change must not disturb the figure
    // MoneyReadConsistencyTests holds equal across three reads.
    [Fact]
    public async Task Aging_Does_Not_Change_The_Outstanding_Total()
    {
        InvoiceDebt(1234.567m, DaysAgo(10));
        PlanDebt(765.433m, DaysAgo(20));

        var row = await RowAsync();

        Assert.Equal(2000.000m, row.TotalOutstanding);
    }
}

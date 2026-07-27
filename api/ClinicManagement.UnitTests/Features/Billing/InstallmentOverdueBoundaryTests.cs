using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Billing;

/// <summary>
/// Pins the overdue boundary: an échéance is late only once its calendar DAY has passed.
/// <para>
/// Due dates are stored at midnight, so the original <c>DueDate &lt; DateTime.UtcNow</c> was true from
/// 00:00 on the due date itself — every échéance was reported overdue a full day early, and the plan page
/// badged one due today « En retard ». These tests fix the boundary in place; they are written against
/// <c>UtcNow</c>-relative dates rather than a frozen clock because the handler reads the clock directly.
/// </para>
/// </summary>
public class InstallmentOverdueBoundaryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICnamBillingCalculator> _cnam = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    // No avoirs in these fixtures. Stated explicitly rather than left to Moq's default, because the
    // batch read returns a dictionary the handlers immediately enumerate.
    private readonly Mock<ICreditNoteRepository> _creditNotes = NoCreditNotes();

    private static Mock<ICreditNoteRepository> NoCreditNotes()
    {
        var mock = new Mock<ICreditNoteRepository>();
        mock.Setup(r => r.GetTotalsForInvoicesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());
        return mock;
    }

    /// <summary>An accepted 500 DT devis whose single unpaid échéance falls on <paramref name="dueDate"/>.</summary>
    private static TreatmentPlan PlanDueOn(DateTime dueDate)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(new[] { ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });
        // The schedule has to be set while the devis is still a Draft; accepting is what turns it into debt,
        // and only a debt-bearing plan reaches the overdue calculation at all.
        plan.SetInstallments(new[] { (dueDate, 500m) });
        plan.Accept("2026-0001");
        return plan;
    }

    private async Task<DateTime?> OldestOverdueFor(DateTime dueDate)
    {
        var patient = new Patient(
            PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(p => p.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        _invoices.Setup(i => i.GetFilteredAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<InvoiceStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Invoice>());
        _plans.Setup(p => p.GetFilteredAsync(
                ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentPlan> { PlanDueOn(dueDate) });
        _cnam.Setup(c => c.ComputeAsync(
                It.IsAny<IReadOnlyCollection<CnamBillingLine>>(), It.IsAny<decimal>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CnamSplit(0m, 500m));

        var handler = new GetPatientBillingSummaryQueryHandler(
            _invoices.Object, _plans.Object, _patients.Object, _creditNotes.Object, _cnam.Object,
            _clinicResolver.Object, NullLogger<GetPatientBillingSummaryQueryHandler>.Instance);

        var result = await handler.Handle(
            new GetPatientBillingSummaryQuery { PatientId = PatientId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value!.OldestOverdueDate;
    }

    // The regression: due TODAY at midnight. The patient still has the whole day, so nothing is overdue.
    [Fact]
    public async Task An_Installment_Due_Today_Is_Not_Overdue()
    {
        var today = DateTime.UtcNow.Date;

        Assert.Null(await OldestOverdueFor(today));
    }

    [Fact]
    public async Task An_Installment_Due_Yesterday_Is_Overdue()
    {
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        Assert.Equal(yesterday, await OldestOverdueFor(yesterday));
    }

    [Fact]
    public async Task An_Installment_Due_Tomorrow_Is_Not_Overdue()
    {
        Assert.Null(await OldestOverdueFor(DateTime.UtcNow.Date.AddDays(1)));
    }

    /// <summary>
    /// The boundary must hold late in the day too. A due date stored at midnight is "behind" the current
    /// instant for all but the first moment of its own day, which is exactly what the instant comparison got
    /// wrong — so an assertion taken at 23:xx has to agree with one taken at 00:xx.
    /// </summary>
    [Fact]
    public async Task Todays_Installment_Is_Not_Overdue_Regardless_Of_The_Time_Of_Day()
    {
        // Same calendar day as "now", but explicitly late in it.
        var todayLate = DateTime.UtcNow.Date.AddHours(23).AddMinutes(59);

        Assert.Null(await OldestOverdueFor(todayLate));
    }
}

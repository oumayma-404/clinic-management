using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
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
/// The « extrait de caisse » — the statement behind the caisse's totals.
///
/// <para><b>What this file is really guarding.</b> La caisse showed three figures with a table of *expenses only*
/// under them: the money-out side was itemised while « Encaissé », the bigger number, was opaque. The tempting fix
/// is a <c>CashMovement</c> table every money path writes to — double bookkeeping, where the day one write site
/// forgets, the statement and the totals disagree and nothing can say which is right. The statement is therefore a
/// <b>read</b> over the same four ledgers the totals sum, and the load-bearing test here is
/// <see cref="The_Movements_Sum_To_The_Caisse_Totals"/>: an assertion that is only possible because of that choice.
/// </para>
///
/// <para>Pairs with <c>MoneyReadConsistencyTests</c>, which holds the caisse and the dashboard equal. Between them,
/// four reads over one fixture must agree.</para>
/// </summary>
public class CaisseLedgerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PlanId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid InvoiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // A window comfortably around every fixture date below, passed explicitly so no test depends on "now".
    private static readonly DateTime From = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToInclusive = new(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IExpenseRepository> _expenses = new();
    private readonly Mock<ICreditNoteRepository> _creditNotes = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private static Patient PatientFixture() => new(
        PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    private static DateTime Day(int day) => new(2026, 7, day, 9, 0, 0, DateTimeKind.Utc);

    private void Wire(
        IReadOnlyList<CaissePaymentRow>? payments = null,
        IReadOnlyList<CaisseInstallmentPaymentRow>? installmentPayments = null,
        IReadOnlyList<CreditNote>? refunds = null,
        IReadOnlyList<Expense>? expenses = null)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        _invoices.Setup(r => r.GetPaymentsBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments ?? Array.Empty<CaissePaymentRow>());
        _plans.Setup(r => r.GetInstallmentPaymentsBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(installmentPayments ?? Array.Empty<CaisseInstallmentPaymentRow>());
        _creditNotes.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refunds ?? Array.Empty<CreditNote>());
        _expenses.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expenses ?? Array.Empty<Expense>());

        // The statement resolves every patient name in one batch — no name means no N+1 to fall back on.
        _patients.Setup(r => r.GetByIdsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, Patient>)(ids.Contains(PatientId)
                    ? new Dictionary<Guid, Patient> { [PatientId] = PatientFixture() }
                    : new Dictionary<Guid, Patient>()));

        // The totals side, wired to whatever the row fixtures imply, so the two can be compared.
        _invoices.Setup(r => r.GetCollectedBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((payments ?? Array.Empty<CaissePaymentRow>()).Where(p => !p.IsVoided).Sum(p => p.Amount));
        _plans.Setup(r => r.GetInstallmentCollectedBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((installmentPayments ?? Array.Empty<CaisseInstallmentPaymentRow>())
                .Where(p => !p.IsVoided).Sum(p => p.Amount));
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((refunds ?? Array.Empty<CreditNote>()).Sum(c => c.Amount));
        _expenses.Setup(r => r.GetTotalBetweenAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((expenses ?? Array.Empty<Expense>()).Sum(e => e.Amount));
    }

    private async Task<CaisseLedgerDto> LedgerAsync()
    {
        var handler = new GetCaisseLedgerQueryHandler(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object, _patients.Object,
            _clinicResolver.Object, NullLogger<GetCaisseLedgerQueryHandler>.Instance);
        var result = await handler.Handle(
            new GetCaisseLedgerQuery { From = From, To = ToInclusive }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private async Task<CaisseSummaryDto> SummaryAsync()
    {
        var handler = new GetCaisseSummaryQueryHandler(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetCaisseSummaryQueryHandler>.Instance);
        var result = await handler.Handle(
            new GetCaisseSummaryQuery { From = From, To = ToInclusive }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static CaissePaymentRow Payment(
        decimal amount, int day, string? number = "2026-0012", bool voided = false, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), InvoiceId, number, PatientId, amount, PaymentMethod.Cash, Day(day),
            voided, voided ? "Erreur de saisie" : null, voided ? "Dr Ben Ali" : null);

    private static CaisseInstallmentPaymentRow Installment(decimal amount, int day, bool voided = false) =>
        new(Guid.NewGuid(), PlanId, "2026-0007", PatientId, amount, PaymentMethod.Cheque, Day(day),
            voided, voided ? "Chèque sans provision" : null, voided ? "Dr Ben Ali" : null);

    private static CreditNote Refund(decimal amount, int day) =>
        new(Guid.NewGuid(), ClinicId, InvoiceId, "2026-0002", amount, "Acte non réalisé",
            PaymentMethod.Cash, Day(day));

    private static Expense ExpenseFixture(decimal amount, int day, string category = "Consommables") =>
        new(Guid.NewGuid(), ClinicId, Day(day), category, amount, PaymentMethod.Card, "Gants et masques");

    // ---- The one that justifies the whole design -----------------------------

    [Fact]
    public async Task The_Movements_Sum_To_The_Caisse_Totals()
    {
        // Every kind present, a voided row in each payment ledger, and non-round figures so a coincidental
        // equality is not possible.
        Wire(
            payments: new[] { Payment(1200.500m, 3), Payment(300.250m, 9), Payment(90m, 11, voided: true) },
            installmentPayments: new[] { Installment(450.750m, 5), Installment(60m, 12, voided: true) },
            refunds: new[] { Refund(180.500m, 14) },
            expenses: new[] { ExpenseFixture(320.125m, 7), ExpenseFixture(75.500m, 20, "Loyer") });

        var ledger = await LedgerAsync();
        var summary = await SummaryAsync();

        var live = ledger.Movements.Where(m => !m.IsVoided).ToList();
        var moneyIn = live.Where(m => m.Direction == CaisseMovementDirection.In).Sum(m => m.Amount);
        var refunded = live.Where(m => m.Kind == CaisseMovementKind.Refund).Sum(m => m.Amount);
        var spent = live.Where(m => m.Kind == CaisseMovementKind.Expense).Sum(m => m.Amount);

        // If this ever fails, the statement is describing money the totals do not agree exists — which is the
        // entire failure mode a `CashMovement` table would have made unfalsifiable.
        Assert.Equal(summary.CashIn, moneyIn);
        Assert.Equal(summary.Refunds, refunded);
        Assert.Equal(summary.CashOut, spent);
        Assert.Equal(summary.Net, moneyIn - refunded - spent);

        // And the last running balance is the period's net, so the column a reader follows down the page lands
        // on the figure printed above it.
        Assert.Equal(summary.Net, ledger.Movements[^1].RunningBalance);
    }

    // ---- Ordering and the running balance -----------------------------------

    [Fact]
    public async Task Movements_Are_Oldest_First()
    {
        Wire(
            payments: new[] { Payment(100m, 20), Payment(100m, 3) },
            expenses: new[] { ExpenseFixture(50m, 11) });

        var ledger = await LedgerAsync();

        // A statement reads forward; the running balance is meaningless in any other order.
        Assert.Equal(new[] { Day(3), Day(11), Day(20) }, ledger.Movements.Select(m => m.OccurredOn));
    }

    [Fact]
    public async Task Ordering_Is_Stable_For_Movements_On_The_Same_Date()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Wire(payments: new[] { Payment(10m, 5, id: second), Payment(10m, 5, id: first) });

        var a = await LedgerAsync();
        var b = await LedgerAsync();

        // Two reads of one window must not shuffle — a statement whose rows move looks like the data changed.
        Assert.Equal(a.Movements.Select(m => m.Id), b.Movements.Select(m => m.Id));
        Assert.Equal(new[] { first, second }, a.Movements.Select(m => m.Id));
    }

    [Fact]
    public async Task A_Voided_Movement_Is_Listed_And_Does_Not_Move_The_Balance()
    {
        Wire(payments: new[] { Payment(500m, 3), Payment(999m, 4, voided: true), Payment(100m, 5) });

        var ledger = await LedgerAsync();

        // § 1 keeps a void visible and struck through, with its motif and its author — hiding it would make the
        // statement useless as the trail it exists to be.
        var voided = Assert.Single(ledger.Movements.Where(m => m.IsVoided));
        Assert.Equal("Erreur de saisie", voided.VoidReason);
        Assert.Equal("Dr Ben Ali", voided.VoidedByName);

        Assert.Equal(500m, ledger.Movements[0].RunningBalance);
        Assert.Equal(500m, voided.RunningBalance);          // unchanged across the voided row
        Assert.Equal(600m, ledger.Movements[^1].RunningBalance);
    }

    [Fact]
    public async Task The_Running_Balance_Falls_On_An_Outflow()
    {
        Wire(
            payments: new[] { Payment(1000m, 2) },
            refunds: new[] { Refund(250m, 3) },
            expenses: new[] { ExpenseFixture(150m, 4) });

        var ledger = await LedgerAsync();

        Assert.Equal(new[] { 1000m, 750m, 600m }, ledger.Movements.Select(m => m.RunningBalance));
    }

    // ---- Per-kind mapping ---------------------------------------------------

    [Fact]
    public async Task Each_Kind_Maps_To_Its_Direction_And_A_French_Label()
    {
        Wire(
            payments: new[] { Payment(100m, 2) },
            installmentPayments: new[] { Installment(200m, 3) },
            refunds: new[] { Refund(50m, 4) },
            expenses: new[] { ExpenseFixture(30m, 5) });

        var byKind = (await LedgerAsync()).Movements.ToDictionary(m => m.Kind);

        Assert.Equal(CaisseMovementDirection.In, byKind[CaisseMovementKind.InvoicePayment].Direction);
        Assert.Equal(CaisseMovementDirection.In, byKind[CaisseMovementKind.InstallmentPayment].Direction);
        Assert.Equal(CaisseMovementDirection.Out, byKind[CaisseMovementKind.Refund].Direction);
        Assert.Equal(CaisseMovementDirection.Out, byKind[CaisseMovementKind.Expense].Direction);

        Assert.Equal("Paiement facture 2026-0012", byKind[CaisseMovementKind.InvoicePayment].Label);
        Assert.Equal("Échéance devis 2026-0007", byKind[CaisseMovementKind.InstallmentPayment].Label);
        Assert.Contains("Avoir 2026-0002", byKind[CaisseMovementKind.Refund].Label);
        Assert.Contains("Consommables", byKind[CaisseMovementKind.Expense].Label);

        // Amounts are always positive — the direction carries the sign, so a reader cannot mistake a refund for
        // income because of a lost minus.
        Assert.All((await LedgerAsync()).Movements, m => Assert.True(m.Amount > 0m));
    }

    [Fact]
    public async Task A_Payment_On_A_Draft_Invoice_Says_So_Instead_Of_Printing_An_Empty_Number()
    {
        Wire(payments: new[] { Payment(100m, 2, number: null) });

        var movement = Assert.Single((await LedgerAsync()).Movements);

        Assert.Null(movement.Reference);
        Assert.Contains("brouillon", movement.Label);
        // The point is that no number is printed — not that the word « facture » is absent.
        Assert.DoesNotContain("2026-", movement.Label);
    }

    [Fact]
    public async Task Patient_Names_Are_Resolved_In_One_Batch()
    {
        Wire(
            payments: new[] { Payment(100m, 2), Payment(200m, 3) },
            installmentPayments: new[] { Installment(300m, 4) });

        var ledger = await LedgerAsync();

        Assert.All(
            ledger.Movements.Where(m => m.PatientId is not null),
            m => Assert.Equal("Jean Dupont", m.PatientName));

        // One call for the whole statement, and the id set is de-duplicated — three rows, one patient.
        _patients.Verify(r => r.GetByIdsAsync(
            ClinicId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- De-duplication and scoping ----------------------------------------

    [Fact]
    public async Task The_Billed_Plan_Exclusion_Reaches_The_Installment_Read()
    {
        var bridgedPlan = Guid.NewGuid();
        Wire();
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (bridgedPlan, InvoiceId, "2026-0031", InvoiceStatus.Issued),
            });

        await LedgerAsync();

        // A bridged devis has its collections carried onto the invoice at issue. Listing them on the plan side too
        // would show the same money twice AND stop the statement summing to the totals.
        _plans.Verify(r => r.GetInstallmentPaymentsBetweenAsync(
            ClinicId,
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(bridgedPlan)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Patient_Outside_The_Clinic_Leaves_The_Name_Blank_Rather_Than_Leaking()
    {
        // The batch read applies the clinic filter itself, so a cross-clinic id simply comes back absent.
        Wire(payments: new[] { Payment(100m, 2) });
        _patients.Setup(r => r.GetByIdsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<Guid, Patient>)new Dictionary<Guid, Patient>());

        var movement = Assert.Single((await LedgerAsync()).Movements);

        Assert.Null(movement.PatientName);
        Assert.Equal(PatientId, movement.PatientId);
    }

    [Fact]
    public async Task An_Empty_Window_Returns_No_Movements_And_A_Zero_Net()
    {
        Wire();

        var ledger = await LedgerAsync();
        var summary = await SummaryAsync();

        Assert.Empty(ledger.Movements);
        Assert.Equal(0m, summary.Net);
        Assert.Equal(From, ledger.FromDate);
        Assert.Equal(ToInclusive, ledger.ToDate);
    }

    [Fact]
    public async Task An_Inverted_Window_Is_Refused_In_French()
    {
        Wire();
        var handler = new GetCaisseLedgerQueryHandler(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object, _patients.Object,
            _clinicResolver.Object, NullLogger<GetCaisseLedgerQueryHandler>.Instance);

        var result = await handler.Handle(
            new GetCaisseLedgerQuery { From = ToInclusive, To = From }, CancellationToken.None);

        // Same guard and same message as the summary — the two must not disagree about which windows are valid.
        Assert.True(result.IsFailure);
        Assert.Equal("La date de fin doit être postérieure à la date de début.", result.Error);
    }

    [Fact]
    public async Task A_Failed_Clinic_Resolution_Is_A_Failure_Not_An_Empty_Statement()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Cabinet introuvable."));

        var handler = new GetCaisseLedgerQueryHandler(
            _invoices.Object, _plans.Object, _expenses.Object, _creditNotes.Object, _patients.Object,
            _clinicResolver.Object, NullLogger<GetCaisseLedgerQueryHandler>.Instance);
        var result = await handler.Handle(new GetCaisseLedgerQuery(), CancellationToken.None);

        // An empty statement and a broken session look identical on screen; only one of them is safe to show.
        Assert.True(result.IsFailure);
        _invoices.Verify(r => r.GetPaymentsBetweenAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

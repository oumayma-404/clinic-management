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
/// The cheque life-cycle (Group B, AC-8…AC-12, B-1, B-2): a cheque can be marked as taken to the bank without
/// voiding the payment that received it.
///
/// <para>
/// <b>The assertion this file exists for is AC-9</b> — that marking moves <i>no figure anywhere</i>. Every other
/// case here would still pass if banking quietly re-dated collected cash, and that is precisely the change that
/// would corrupt every historical caisse figure a practice has already read and reconciled. La caisse counts a
/// cheque on the day it was received; Group B deliberately does not touch that.
/// </para>
/// </summary>
public class ChequeBankedStampTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PlanId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid InstallmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid InvoiceId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly DateTime PaidOn = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ the domain: the stamp itself

    // [AC-8] Marking records the moment, the actor and their name — the three things that make it a trail rather
    // than a flag somebody flipped.
    [Fact]
    public void Marking_A_Cheque_Stamps_The_Moment_And_The_Actor()
    {
        var invoice = IssuedInvoiceWithChequePayment();
        var payment = invoice.Payments.Single();

        invoice.SetPaymentBanked(payment.Id, banked: true, "local|42", "Dr Ben Salah");

        Assert.NotNull(payment.ChequeBankedOn);
        Assert.Equal("local|42", payment.ChequeBankedByUserId);
        Assert.Equal("Dr Ben Salah", payment.ChequeBankedByName);
    }

    /// <summary>
    /// [AC-9] The assertion the whole feature turns on: banking is a tracking state, so every money figure the
    /// invoice carries is byte-identical before and after. If this ever fails, la caisse, the dashboard and the
    /// patient's solde have all silently moved with it.
    /// </summary>
    [Fact]
    public void Marking_A_Cheque_Moves_No_Money_On_The_Invoice()
    {
        var invoice = IssuedInvoiceWithChequePayment();
        var payment = invoice.Payments.Single();

        var collectedBefore = invoice.AmountCollected;
        var statusBefore = invoice.Status;
        var outstandingBefore = invoice.Outstanding;
        var amountBefore = payment.Amount;
        var paidOnBefore = payment.PaidOn;

        invoice.SetPaymentBanked(payment.Id, banked: true, "local|42", "Dr Ben Salah");

        Assert.Equal(collectedBefore, invoice.AmountCollected);
        Assert.Equal(statusBefore, invoice.Status);
        Assert.Equal(outstandingBefore, invoice.Outstanding);
        Assert.Equal(amountBefore, payment.Amount);
        // The money date in particular: re-dating a payment onto its banking day is the one change that would
        // move a figure in a closed month.
        Assert.Equal(paidOnBefore, payment.PaidOn);
        Assert.False(payment.IsVoided);
    }

    // [AC-9] The same on the plan track, where the denormalized totals are recomputed from the ledger and so
    // would be the ones to drift.
    [Fact]
    public void Marking_An_Installment_Cheque_Moves_No_Money_On_The_Plan()
    {
        var plan = PlanWithChequeInstallmentPayment();
        var installment = plan.Installments.Single();
        var payment = installment.Payments.Single();

        var paidBefore = installment.AmountPaid;
        var lastMethodBefore = installment.LastMethod;
        var lastPaidOnBefore = installment.LastPaidOn;

        plan.SetInstallmentPaymentBanked(installment.Id, payment.Id, banked: true, "local|42", "Dr Ben Salah");

        Assert.Equal(paidBefore, installment.AmountPaid);
        Assert.Equal(lastMethodBefore, installment.LastMethod);
        Assert.Equal(lastPaidOnBefore, installment.LastPaidOn);
    }

    /// <summary>
    /// [AC-10] Un-marking is supported — a cheque returned unpaid by the bank is the ordinary case — and both
    /// directions touch the aggregate root, which is what puts them in the audit ledger:
    /// <c>AuditSaveChangesInterceptor</c> records <b>aggregate roots</b>, so a child-only mutation would leave no
    /// row at all and « qui a démarqué ce chèque ? » would have no answer anywhere.
    /// </summary>
    [Fact]
    public void Un_Marking_Clears_The_Stamp_And_Touches_The_Root_So_It_Is_Audited()
    {
        var invoice = IssuedInvoiceWithChequePayment();
        var payment = invoice.Payments.Single();
        invoice.SetPaymentBanked(payment.Id, banked: true, "local|42", "Dr Ben Salah");

        var touchedAfterMarking = invoice.UpdatedAt;

        invoice.SetPaymentBanked(payment.Id, banked: false, "local|7", "Secrétaire");

        Assert.Null(payment.ChequeBankedOn);
        Assert.Null(payment.ChequeBankedByUserId);
        Assert.Null(payment.ChequeBankedByName);
        Assert.NotNull(invoice.UpdatedAt);
        Assert.True(invoice.UpdatedAt >= touchedAfterMarking);
    }

    // Espèces are already in the drawer and a card settles itself, so the mark would describe nothing — and it
    // would put a row that is not a cheque into a list of cheques.
    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Transfer)]
    public void A_Non_Cheque_Payment_Cannot_Be_Marked_Banked(PaymentMethod method)
    {
        var invoice = IssuedInvoiceWithChequePayment(method);
        var payment = invoice.Payments.Single();

        var ex = Assert.Throws<InvalidOperationException>(
            () => invoice.SetPaymentBanked(payment.Id, banked: true, "local|42", "Dr Ben Salah"));

        Assert.Contains("chèque", ex.Message);
    }

    // A double-click must not rewrite the original stamp with a later moment and a different actor — the same
    // idempotency contract `VoidPayment` states.
    [Fact]
    public void Marking_A_Cheque_Twice_Is_Refused()
    {
        var invoice = IssuedInvoiceWithChequePayment();
        var payment = invoice.Payments.Single();
        invoice.SetPaymentBanked(payment.Id, banked: true, "local|42", "Dr Ben Salah");

        Assert.Throws<InvalidOperationException>(
            () => invoice.SetPaymentBanked(payment.Id, banked: true, "local|7", "Quelqu'un d'autre"));
    }

    // [AC-12] A voided payment was never received, so there is no cheque to take anywhere.
    [Fact]
    public void A_Voided_Payment_Cannot_Be_Marked_Banked()
    {
        var invoice = IssuedInvoiceWithChequePayment();
        var payment = invoice.Payments.Single();
        invoice.VoidPayment(payment.Id, "Erreur de saisie", creditedTotal: 0m, "local|42", "Dr Ben Salah");

        Assert.Throws<InvalidOperationException>(
            () => invoice.SetPaymentBanked(payment.Id, banked: true, "local|42", "Dr Ben Salah"));
    }

    // ------------------------------------------------------------------ B-1 / B-2: the devis→facture bridge

    /// <summary>
    /// [B-1] The bridge carries the banked stamp with the cheque. Without it a cheque banked in September and
    /// billed in October <b>reappears</b> under « à encaisser » the moment the plan side stops being counted —
    /// and re-marking it would record today rather than the day it was actually deposited.
    /// </summary>
    [Fact]
    public void The_Bridge_Carries_The_Banked_Stamp_Onto_The_Invoice_Payment()
    {
        var plan = PlanWithChequeInstallmentPayment();
        var installment = plan.Installments.Single();
        var source = installment.Payments.Single();
        plan.SetInstallmentPaymentBanked(installment.Id, source.Id, banked: true, "local|42", "Dr Ben Salah");

        var invoice = IssuedInvoice();
        invoice.RecordPayment(
            source.Amount, source.Method, source.PaidOn, source.Id,
            source.ToChequeDetails(), source.ToBankedStamp());

        var carried = invoice.Payments.Single();
        Assert.Equal(source.ChequeBankedOn, carried.ChequeBankedOn);
        Assert.Equal("Dr Ben Salah", carried.ChequeBankedByName);
        // The cheque's identity travels too — the two halves are useless apart.
        Assert.Equal(source.ChequeNumber, carried.ChequeNumber);
        Assert.Equal(source.ChequeDueDate, carried.ChequeDueDate);
    }

    // [B-1] A cheque still held crosses the bridge with no stamp — the carry must not invent one.
    [Fact]
    public void The_Bridge_Carries_No_Stamp_For_A_Cheque_Still_Held()
    {
        var plan = PlanWithChequeInstallmentPayment();
        var source = plan.Installments.Single().Payments.Single();

        var invoice = IssuedInvoice();
        invoice.RecordPayment(
            source.Amount, source.Method, source.PaidOn, source.Id,
            source.ToChequeDetails(), source.ToBankedStamp());

        Assert.Null(invoice.Payments.Single().ChequeBankedOn);
    }

    /// <summary>
    /// [B-2] Confirmed rather than assumed: a bridge invoice holding a non-voided payment <b>cannot be
    /// cancelled</b>, so there is no route by which the plan side comes back holding a cheque the invoice also
    /// holds. The avoir is the only correction, and it leaves both rows in place.
    /// </summary>
    [Fact]
    public void A_Bridge_Invoice_Holding_A_Live_Payment_Cannot_Be_Cancelled()
    {
        var invoice = IssuedInvoiceWithChequePayment();

        Assert.False(invoice.CanCancel);
    }

    // ------------------------------------------------------------------ the read: default view, filter, buckets

    /// <summary>
    /// [AC-8] The default list is what the clinic still holds; a banked cheque is reachable only by asking for it.
    /// Both directions are asserted from one fixture, because « it left the list » and « it is findable » are the
    /// two halves of the same claim and a screen can satisfy one while failing the other.
    /// </summary>
    [Fact]
    public async Task A_Banked_Cheque_Leaves_The_Default_View_And_Is_Found_Under_Encaisses()
    {
        var held = ChequeRow(Guid.NewGuid(), 100m, bankedOn: null);
        var banked = ChequeRow(Guid.NewGuid(), 250m, bankedOn: new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc));

        var outstanding = await RunQuery(new GetChequesDueQuery(), held, banked);
        Assert.Equal(held.PaymentId, Assert.Single(outstanding.Items).Id);

        var encaisses = await RunQuery(new GetChequesDueQuery { Banked = true }, held, banked);
        var row = Assert.Single(encaisses.Items);
        Assert.Equal(banked.PaymentId, row.Id);
        Assert.True(row.Banked);
        Assert.Equal(banked.ChequeBankedOn, row.BankedOn);
        Assert.Equal("Dr Ben Salah", row.BankedByName);
    }

    // Omitting the parameter and sending `false` are the same request: a client that does not know about the
    // filter must still get the to-do list rather than everything.
    [Fact]
    public async Task Omitting_The_Filter_Means_Outstanding()
    {
        var held = ChequeRow(Guid.NewGuid(), 100m, bankedOn: null);
        var banked = ChequeRow(Guid.NewGuid(), 250m, bankedOn: PaidOn);

        var omitted = await RunQuery(new GetChequesDueQuery(), held, banked);
        var explicitly = await RunQuery(new GetChequesDueQuery { Banked = false }, held, banked);

        Assert.Equal(omitted.Items.Select(i => i.Id), explicitly.Items.Select(i => i.Id));
    }

    /// <summary>
    /// [AC-11] The four bucket counts describe the <b>outstanding</b> set only — « combien me reste-t-il à
    /// encaisser ? » — and they say the same thing whichever side of the filter is being viewed. A header that
    /// changed meaning with the filter would make the figure unreadable.
    /// </summary>
    [Fact]
    public async Task The_Buckets_Count_Outstanding_Cheques_Only_On_Both_Sides_Of_The_Filter()
    {
        var held = ChequeRow(Guid.NewGuid(), 100m, bankedOn: null);
        var banked = ChequeRow(Guid.NewGuid(), 250m, bankedOn: PaidOn);

        var outstanding = await RunQuery(new GetChequesDueQuery(), held, banked);
        var encaisses = await RunQuery(new GetChequesDueQuery { Banked = true }, held, banked);

        Assert.Equal(1, outstanding.Groups.Total.Count);
        Assert.Equal(100m, outstanding.Groups.Total.Amount);
        Assert.Equal(outstanding.Groups.Total.Count, encaisses.Groups.Total.Count);
        Assert.Equal(outstanding.Groups.Total.Amount, encaisses.Groups.Total.Amount);
    }

    /// <summary>
    /// [AC-12] Voiding removes the cheque from every view whatever its banked state. The repositories exclude
    /// voided rows, so this pins the contract at the seam the query depends on rather than re-testing SQL: a
    /// voided row never reaches the handler, and therefore cannot be listed under either filter.
    /// </summary>
    [Fact]
    public async Task A_Voided_Cheque_Is_In_Neither_View()
    {
        // Both repositories return nothing, which is what voiding does to the row upstream of here.
        var outstanding = await RunQuery(new GetChequesDueQuery());
        var encaisses = await RunQuery(new GetChequesDueQuery { Banked = true });

        Assert.Empty(outstanding.Items);
        Assert.Empty(encaisses.Items);
        Assert.Equal(0, outstanding.Groups.Total.Count);
    }

    // [B-1] The plan half of the list carries the échéance id, without which it could be shown and never acted
    // on: an InstallmentPayment is only addressable as {plan, installment, payment}.
    [Fact]
    public async Task An_Installment_Cheque_Carries_The_Ids_Its_Write_Route_Needs()
    {
        var row = new CaisseInstallmentPaymentRow(
            Guid.NewGuid(), PlanId, InstallmentId, "2026-0007", PatientId,
            300m, PaymentMethod.Cheque, PaidOn, IsVoided: false, VoidReason: null, VoidedByName: null,
            "4512873", "BIAT", new DateTime(2026, 9, 15));

        var result = await RunQuery(new GetChequesDueQuery(), planRows: new[] { row });

        var dto = Assert.Single(result.Items);
        Assert.Equal(PlanId, dto.TargetId);
        Assert.Equal(InstallmentId, dto.InstallmentId);
    }

    // An invoice cheque has no échéance, and the client branches on that to pick its route.
    [Fact]
    public async Task An_Invoice_Cheque_Carries_No_Installment_Id()
    {
        var result = await RunQuery(new GetChequesDueQuery(), ChequeRow(Guid.NewGuid(), 100m, bankedOn: null));

        Assert.Null(Assert.Single(result.Items).InstallmentId);
    }

    // ------------------------------------------------------------------ fixtures

    private static Invoice IssuedInvoice()
    {
        var invoice = new Invoice(InvoiceId, ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 1000m) });
        invoice.Issue("2026-0001");
        return invoice;
    }

    private static Invoice IssuedInvoiceWithChequePayment(PaymentMethod method = PaymentMethod.Cheque)
    {
        var invoice = IssuedInvoice();

        // Details only for a cheque: `ChequeDetails.For` refuses them on any other method, which is the sibling
        // invariant this file's non-cheque cases are about.
        var cheque = method == PaymentMethod.Cheque
            ? ChequeDetails.For(method, "4512873", "BIAT", new DateTime(2026, 9, 15))
            : null;

        invoice.RecordPayment(250m, method, PaidOn, sourceInstallmentPaymentId: null, cheque);
        return invoice;
    }

    private static TreatmentPlan PlanWithChequeInstallmentPayment()
    {
        var plan = new TreatmentPlan(PlanId, ClinicId, PatientId, "Traitement");
        plan.SetItems(new[]
        {
            ("Couronne", 1000m, (IReadOnlyList<int>)Array.Empty<int>())
        });
        plan.SetInstallments(new[] { (new DateTime(2026, 9, 1), 1000m) });
        plan.Accept("2026-0007");

        // The échéance's id is assigned by the aggregate, so it is read back rather than chosen — a fixture that
        // invented one would be asserting against a row the plan does not have.
        plan.RecordInstallmentPayment(
            plan.Installments.Single().Id, 300m, PaymentMethod.Cheque, PaidOn,
            ChequeDetails.For(PaymentMethod.Cheque, "4512873", "BIAT", new DateTime(2026, 9, 15)));
        return plan;
    }

    private static CaissePaymentRow ChequeRow(Guid paymentId, decimal amount, DateTime? bankedOn) =>
        new(paymentId, InvoiceId, "2026-0001", PatientId, amount, PaymentMethod.Cheque, PaidOn,
            IsVoided: false, VoidReason: null, VoidedByName: null,
            "4512873", "BIAT", new DateTime(2026, 9, 15),
            bankedOn, bankedOn.HasValue ? "Dr Ben Salah" : null);

    private static Task<ChequesDueDto> RunQuery(
        GetChequesDueQuery query,
        params CaissePaymentRow[] invoiceRows) => RunQuery(query, invoiceRows, Array.Empty<CaisseInstallmentPaymentRow>());

    private static Task<ChequesDueDto> RunQuery(
        GetChequesDueQuery query,
        IReadOnlyList<CaisseInstallmentPaymentRow> planRows) =>
        RunQuery(query, Array.Empty<CaissePaymentRow>(), planRows);

    private static async Task<ChequesDueDto> RunQuery(
        GetChequesDueQuery query,
        IReadOnlyList<CaissePaymentRow> invoiceRows,
        IReadOnlyList<CaisseInstallmentPaymentRow> planRows)
    {
        var invoices = new Mock<IInvoiceRepository>();
        invoices
            .Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>());
        invoices
            .Setup(r => r.GetChequePaymentsAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoiceRows);

        var plans = new Mock<ITreatmentPlanRepository>();
        plans
            .Setup(r => r.GetInstallmentChequePaymentsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(planRows);

        var patients = new Mock<IPatientRepository>();
        patients
            .Setup(r => r.GetByIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient>());

        var clinicResolver = new Mock<ICurrentClinicResolver>();
        clinicResolver
            .Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var handler = new GetChequesDueQueryHandler(
            invoices.Object, plans.Object, patients.Object, clinicResolver.Object,
            NullLogger<GetChequesDueQueryHandler>.Instance);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }
}

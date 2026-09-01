using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [AC-40][AC-41][AC-42][AC-43][AC-44][AC-45] An avoir stops being write-only.
///
/// <para>
/// Before this slice <c>ICreditNoteRepository</c> had no read path at all — no get, no list, only a numbering
/// probe, two aggregate sums and <c>AddAsync</c>. Once established, an avoir was invisible in every screen and
/// every document, and the one place its amount still mattered — « Total encaissé » on <c>/factures</c> — had
/// two code paths, only one of which subtracted it. The branch that did not was the one the page loads with
/// both date filters empty, i.e. the figure nearly every user actually sees.
/// </para>
/// </summary>
public class CreditNoteReadTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime PaidOn = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RefundedOn = new(2026, 3, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICreditNoteRepository> _creditNotes = new();

    // « Total encaissé » reads the devis instalment ledger too since J5. Left unstubbed on purpose: it returns
    // 0, so these two cases keep testing exactly what they were written for — that the avoir is netted in BOTH
    // branches — without a plan figure muddying the expected 350.
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public CreditNoteReadTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        _patients.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<PatientListSort>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Patient>()).AsPage());
        // ⚠️ Two reads the list and revenue handlers grew after this file was written, both returning a
        // collection — so unstubbed they hand back **null**, the handler dereferences it, and the swallowed
        // NullReferenceException surfaces as a French Result.Failure. Every case here then failed on
        // `Assert.True(result.IsSuccess)`, which says nothing about the missing stub.
        // `GetByIdsAsync` replaced the whole-clinic patient load (list-pagination: names are resolved over the
        // page now); `GetTreatmentPlanLinksAsync` feeds PlanBillingRules' billed-plan de-dup.
        _patients.Setup(r => r.GetByIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient>());
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
        _creditNotes.Setup(r => r.GetTotalsForInvoicesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());
        _creditNotes.Setup(r => r.GetByInvoiceIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CreditNote>());
    }

    /// <summary>An issued invoice of <paramref name="total"/> DT HT, fully collected. No VAT, no stamp.</summary>
    private static Invoice PaidInvoice(decimal total, Guid? clinicId = null)
    {
        var invoice = new Invoice(Guid.NewGuid(), clinicId ?? ClinicId, PatientId);
        invoice.SetLines(new[] { ("Couronne", 1, total) });
        invoice.Issue("2026-0001");
        invoice.RecordPayment(total, PaymentMethod.Cash, PaidOn);
        return invoice;
    }

    private static CreditNote Avoir(Invoice invoice, decimal amount, string number = "2026-0001") =>
        new(Guid.NewGuid(), invoice.ClinicId, invoice.Id, number, amount, "Acte non réalisé",
            PaymentMethod.Cash, RefundedOn);

    // ---------------------------------------------------------------- reads

    // [AC-40] The detail read carries the avoirs themselves — this is the only surface that shows one.
    [Fact]
    public async Task Invoice_Detail_Carries_Its_Avoirs()
    {
        var invoice = PaidInvoice(600m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _creditNotes.Setup(r => r.GetByInvoiceIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Avoir(invoice, 250m) });

        var handler = new GetInvoiceQueryHandler(
            _invoices.Object, _patients.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoiceQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoiceQuery { Id = invoice.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(250m, dto.CreditedTotal);
        var avoir = Assert.Single(dto.CreditNotes);
        Assert.Equal("2026-0001", avoir.Number);
        Assert.Equal("Acte non réalisé", avoir.Reason);
        Assert.Equal("Cash", avoir.Method);
        Assert.Equal(RefundedOn, avoir.RefundedOn);
    }

    // [AC-41] The list badges the credited total without loading the avoirs — one grouped read, no N+1.
    [Fact]
    public async Task Invoice_List_Carries_The_Credited_Total()
    {
        var invoice = PaidInvoice(600m);
        _invoices.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<InvoiceStatus?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { invoice }).AsPage());
        _creditNotes.Setup(r => r.GetTotalsForInvoicesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal> { [invoice.Id] = 250m });

        // L9 — the practitioner-roster read, mocked empty. That reproduces this test's ORIGINAL behaviour exactly:
        // with no roster, no `DoctorName` resolves and each DTO carries null, which is what it carried before the
        // column existed. Attribution has its own tests rather than being smuggled into these.
        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Doctor>());

        var handler = new GetInvoicesQueryHandler(
            _invoices.Object, _patients.Object, _creditNotes.Object, doctors.Object, _clinicResolver.Object,
            NullLogger<GetInvoicesQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value!.Items);
        Assert.Equal(250m, dto.CreditedTotal);
        Assert.Empty(dto.CreditNotes);   // the list does not pay for the avoirs it doesn't render
    }

    // [AC-42] An avoir on another clinic's invoice is unreachable — isolation runs through the invoice.
    [Fact]
    public async Task Foreign_Invoice_Avoirs_Are_NotFound()
    {
        var foreign = PaidInvoice(600m, OtherClinicId);
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new GetInvoiceCreditNotesQueryHandler(
            _invoices.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoiceCreditNotesQueryHandler>.Instance);

        var result = await handler.Handle(
            new GetInvoiceCreditNotesQuery { InvoiceId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Facture introuvable.", result.Error);
        _creditNotes.Verify(r => r.GetByInvoiceIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- « Total encaissé »

    // [AC-43] The no-period branch nets avoirs. This is the defect: /factures loads with both date filters
    // empty, so this branch produced the headline figure, and it summed AmountCollected without subtracting
    // a single avoir — permanently overstating cash the clinic had already given back.
    [Fact]
    public async Task Revenue_Without_A_Period_Nets_Avoirs()
    {
        var invoice = PaidInvoice(600m);
        _invoices.Setup(r => r.GetFilteredAsync(
                ClinicId, null, null, null, null, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { invoice }).AsPage());
        _creditNotes.Setup(r => r.GetTotalsForInvoicesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal> { [invoice.Id] = 250m });

        var handler = new GetInvoiceRevenueQueryHandler(
            _invoices.Object, _plans.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoiceRevenueQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoiceRevenueQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(350m, result.Value!.TotalCollected);   // 600 collected − 250 refunded
    }

    // [AC-43] …and the windowed branch, which already netted, still agrees with it.
    [Fact]
    public async Task Revenue_Over_A_Period_Nets_Avoirs()
    {
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
        var invoice = PaidInvoice(600m);

        _invoices.Setup(r => r.GetFilteredAsync(
                ClinicId, from, to, null, null, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { invoice }).AsPage());
        _invoices.Setup(r => r.GetCollectedBetweenAsync(ClinicId, from, to, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(600m);
        _creditNotes.Setup(r => r.GetRefundedBetweenAsync(ClinicId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(250m);

        var handler = new GetInvoiceRevenueQueryHandler(
            _invoices.Object, _plans.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoiceRevenueQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoiceRevenueQuery { From = from, To = to }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(350m, result.Value!.TotalCollected);
    }

    // ---------------------------------------------------------------- creation guards

    // [AC-44] An unparseable method used to be silently dropped to null: a typo produced an avoir with no
    // recorded means of refund and nobody was told.
    [Fact]
    public async Task Unparseable_Method_Is_Rejected_Not_Dropped()
    {
        var invoice = PaidInvoice(600m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var result = await CreateAvoirAsync(new CreateCreditNoteCommand
        {
            InvoiceId = invoice.Id, Amount = 100m, Reason = "Erreur", Method = "Bitcoin", RefundedOn = RefundedOn,
        });

        Assert.True(result.IsFailure);
        Assert.Equal("Mode de remboursement invalide.", result.Error);
        _creditNotes.Verify(r => r.AddAsync(It.IsAny<CreditNote>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-44] A refund dated in the future would count in the balance today and be absent from la caisse until
    // its date arrived — the same divergence PaymentDateRules closes for payments.
    [Fact]
    public async Task Future_Refund_Date_Is_Rejected()
    {
        var invoice = PaidInvoice(600m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var result = await CreateAvoirAsync(new CreateCreditNoteCommand
        {
            InvoiceId = invoice.Id, Amount = 100m, Reason = "Erreur",
            RefundedOn = DateTime.UtcNow.AddDays(3),
        });

        Assert.True(result.IsFailure);
        Assert.Contains("futur", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // [AC-44] An invoice with nothing collected has nothing to credit — and the refusal now comes from the
    // same predicate the UI is handed (CanCreateCreditNote), so the button and the endpoint cannot disagree.
    [Fact]
    public async Task Avoir_On_An_Uncollected_Invoice_Is_Refused()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Couronne", 1, 600m) });
        invoice.Issue("2026-0002");
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        Assert.False(invoice.CanCreateCreditNote);

        var result = await CreateAvoirAsync(new CreateCreditNoteCommand
        {
            InvoiceId = invoice.Id, Amount = 100m, Reason = "Erreur", RefundedOn = RefundedOn,
        });

        Assert.True(result.IsFailure);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Result<Application.DTOs.CreditNoteDto>> CreateAvoirAsync(CreateCreditNoteCommand command)
    {
        var handler = new CreateCreditNoteCommandHandler(
            _invoices.Object, _creditNotes.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<CreateCreditNoteCommandHandler>.Instance);
        return await handler.Handle(command, CancellationToken.None);
    }
}

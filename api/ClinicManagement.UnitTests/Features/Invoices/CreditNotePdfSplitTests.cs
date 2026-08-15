using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [J6] An avoir reverses the TVA that was actually <b>charged</b>.
///
/// <para>
/// The defect was arithmetic: the query de-VATed the whole credited TTC, but the <b>timbre fiscal sits outside
/// the VAT base</b> — a note's TTC is <c>ht + vat + stamp</c>, so dividing all of it by <c>1 + rate</c>
/// attributes a slice of the 1 DT stamp to the VAT base and over-reports the tax being reversed. On the review's
/// case (100 DT HT, 7 %, 1 DT stamp) a full-value avoir declared HT 100,935 + TVA 7,065 instead of
/// HT 100,000 + TVA 7,000 + timbre 1,000. A figure an avoir invents is a figure the clinic has to defend.
/// </para>
/// <para>
/// The fix derives the split <b>proportionally from the invoice's frozen posture</b> rather than re-deriving it
/// from a rate, which is also what makes a partial avoir correct. These tests assert the captured
/// <see cref="AvoirPdfData"/> rather than the rendered PDF: the arithmetic is the defect, and there is no text
/// extraction available to read a QuestPDF document back.
/// </para>
/// </summary>
public class CreditNotePdfSplitTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime RefundedOn = new(2026, 3, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ICreditNoteRepository> _creditNotes = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPdfGenerationService> _pdf = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    /// <summary>What the handler handed the renderer — the thing under test.</summary>
    private AvoirPdfData? _captured;

    public CreditNotePdfSplitTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.Models.Result<Guid>.Success(ClinicId));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        _pdf.Setup(p => p.GenerateAvoirPdfAsync(It.IsAny<AvoirPdfData>(), It.IsAny<CancellationToken>()))
            .Callback((AvoirPdfData data, CancellationToken _) => _captured = data)
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    /// <summary>
    /// Reconstructs a <b>historical</b> invoice — one issued before TVA and the timbre fiscal were dropped from
    /// the product — by writing the frozen tax columns directly.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Reflection is the point, not a shortcut.</b> No code path can mint a taxed invoice any more:
    /// <c>Issue(number)</c> takes no tax and leaves the three columns at zero. But rows issued *before* that
    /// change still carry 7 % and 1,000 DT, they are numbered legal documents that must keep rendering with the
    /// figures they were really issued with, and an avoir raised against one still has to split across
    /// HT / TVA / timbre correctly. The only way such an invoice comes into existence now is EF materialising a
    /// stored row — which also bypasses the constructor — so reproducing that here is faithful rather than a
    /// trick. Delete these cases only when no pre-change invoice can still be credited.
    /// </remarks>
    private static void FreezeHistoricalTax(Invoice invoice, decimal vatRate, decimal stamp)
    {
        static void Set(Invoice target, string property, object value) =>
            typeof(Invoice).GetProperty(property)!.SetValue(target, value);

        var vat = InvoiceCalculator.RoundMoney(invoice.TotalHt * vatRate / 100m);
        Set(invoice, nameof(Invoice.VatApplicable), vatRate > 0m);
        Set(invoice, nameof(Invoice.VatRate), vatRate);
        Set(invoice, nameof(Invoice.StampDutyAmount), stamp);
        Set(invoice, nameof(Invoice.TotalVat), vat);
        Set(invoice, nameof(Invoice.TotalTtc), InvoiceCalculator.RoundMoney(invoice.TotalHt + vat + stamp));
    }

    /// <summary>
    /// The review's exact case, as a historical note: 100 DT HT at 7 % TVA with the 1,000 DT timbre — TTC 108,000.
    /// </summary>
    private Invoice ReviewCaseInvoice()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Couronne", 1, 100m) });
        invoice.Issue("2026-0007");
        FreezeHistoricalTax(invoice, vatRate: 7m, stamp: 1.000m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    private CreditNote Avoir(Invoice invoice, decimal amount)
    {
        var note = new CreditNote(
            Guid.NewGuid(), invoice.ClinicId, invoice.Id, "2026-0001", amount,
            "Acte non réalisé", PaymentMethod.Cash, RefundedOn);
        _creditNotes.Setup(r => r.GetByIdAsync(note.Id, It.IsAny<CancellationToken>())).ReturnsAsync(note);
        return note;
    }

    private async Task<AvoirPdfData> RenderAsync(CreditNote note)
    {
        var handler = new GetCreditNotePdfQueryHandler(
            _creditNotes.Object, _invoices.Object, _clinics.Object, _patients.Object,
            _pdf.Object, _clinicResolver.Object, NullLogger<GetCreditNotePdfQueryHandler>.Instance);

        var result = await handler.Handle(new GetCreditNotePdfQuery { Id = note.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_captured);
        return _captured!;
    }

    // [J6] The review's case, pinned to the millime. This is the assertion the whole item exists for.
    [Fact]
    public async Task A_Full_Value_Avoir_Reverses_Exactly_What_Was_Charged()
    {
        var invoice = ReviewCaseInvoice();
        // Confirm the fixture before trusting the result: 100 HT + 7 TVA + 1 timbre = 108 TTC.
        Assert.Equal(100.000m, invoice.TotalHt);
        Assert.Equal(7.000m, invoice.TotalVat);
        Assert.Equal(1.000m, invoice.StampDutyAmount);
        Assert.Equal(108.000m, invoice.TotalTtc);

        var data = await RenderAsync(Avoir(invoice, invoice.TotalTtc));

        Assert.Equal(100.000m, data.AmountHt);
        Assert.Equal(7.000m, data.AmountVat);
        Assert.Equal(1.000m, data.AmountStamp);
        Assert.Equal(108.000m, data.AmountTtc);
    }

    // [J6] The old behaviour, stated as a NEGATIVE so the test says what it is protecting against. De-VATing the
    // whole TTC gave 108/1.07 = 100,935 HT and 7,065 TVA — both wrong, and the TVA wrong in the direction that
    // over-reports tax the clinic never charged.
    [Fact]
    public async Task The_Whole_Ttc_Is_Never_DeVated()
    {
        var invoice = ReviewCaseInvoice();

        var data = await RenderAsync(Avoir(invoice, invoice.TotalTtc));

        Assert.NotEqual(100.935m, data.AmountHt);
        Assert.NotEqual(7.065m, data.AmountVat);
    }

    // [J6] The printed lines must sum EXACTLY to « Montant remboursé ». A document whose parts do not add up to
    // its own total is not usable, which is why HT is taken as the remainder rather than rounded a third time.
    [Theory]
    [InlineData(108.000)]   // full value
    [InlineData(54.000)]    // exactly half
    [InlineData(10.000)]    // a small partial
    [InlineData(0.001)]     // one millime — the rounding edge
    [InlineData(107.999)]   // one millime short of the whole
    public async Task The_Three_Components_Always_Sum_To_The_Credited_Total(decimal credited)
    {
        var invoice = ReviewCaseInvoice();

        var data = await RenderAsync(Avoir(invoice, credited));

        Assert.Equal(credited, data.AmountTtc);
        Assert.Equal(data.AmountTtc, data.AmountHt + data.AmountVat + data.AmountStamp);
    }

    // [J6] A partial avoir credits a PROPORTION of each component — half the note is half the TVA and half the
    // timbre, not the full stamp reversed against a part-refund.
    [Fact]
    public async Task A_Half_Value_Avoir_Halves_Every_Component()
    {
        var invoice = ReviewCaseInvoice();

        var data = await RenderAsync(Avoir(invoice, 54.000m));

        Assert.Equal(50.000m, data.AmountHt);
        Assert.Equal(3.500m, data.AmountVat);
        Assert.Equal(0.500m, data.AmountStamp);
    }

    // [J6] A note issued with no VAT has no VAT to reverse — the split must not manufacture one from a rate that
    // was never applied. A historical row that carried only the timbre, which many pre-J11 notes do.
    [Fact]
    public async Task A_Non_Vat_Invoice_Yields_No_Vat_On_The_Avoir()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });
        invoice.Issue("2026-0008");
        FreezeHistoricalTax(invoice, vatRate: 0m, stamp: 1.000m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var data = await RenderAsync(Avoir(invoice, invoice.TotalTtc));

        Assert.Equal(0m, data.AmountVat);
        Assert.Equal(1.000m, data.AmountStamp);
        Assert.Equal(100.000m, data.AmountHt);
        Assert.False(data.VatApplicable);
    }

    // [J6] The avoir carries the invoice's FROZEN rate, not the clinic's current one — a note issued at 7 % must
    // still reverse 7 % after a finance law moves the rate.
    [Fact]
    public async Task The_Avoir_Reports_The_Invoices_Frozen_Rate()
    {
        var invoice = ReviewCaseInvoice();

        var data = await RenderAsync(Avoir(invoice, invoice.TotalTtc));

        Assert.True(data.VatApplicable);
        Assert.Equal(7m, data.VatRate);
    }

    // [J6][edge] A dangling invoice reference fails loudly rather than rendering an avoir with no posture to
    // apportion — the split can only come from the corrected note.
    [Fact]
    public async Task A_Missing_Corrected_Invoice_Refuses_To_Render()
    {
        var note = new CreditNote(
            Guid.NewGuid(), ClinicId, Guid.NewGuid(), "2026-0002", 50m,
            "Motif", PaymentMethod.Cash, RefundedOn);
        _creditNotes.Setup(r => r.GetByIdAsync(note.Id, It.IsAny<CancellationToken>())).ReturnsAsync(note);
        _invoices.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);

        var handler = new GetCreditNotePdfQueryHandler(
            _creditNotes.Object, _invoices.Object, _clinics.Object, _patients.Object,
            _pdf.Object, _clinicResolver.Object, NullLogger<GetCreditNotePdfQueryHandler>.Instance);

        var result = await handler.Handle(new GetCreditNotePdfQuery { Id = note.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _pdf.Verify(p => p.GenerateAvoirPdfAsync(
            It.IsAny<AvoirPdfData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [J6] Another clinic's avoir is unreachable — isolation runs through the credit note.
    [Fact]
    public async Task A_Foreign_Avoir_Is_NotFound()
    {
        var note = new CreditNote(
            Guid.NewGuid(), OtherClinicId, Guid.NewGuid(), "2026-0003", 50m,
            "Motif", PaymentMethod.Cash, RefundedOn);
        _creditNotes.Setup(r => r.GetByIdAsync(note.Id, It.IsAny<CancellationToken>())).ReturnsAsync(note);

        var handler = new GetCreditNotePdfQueryHandler(
            _creditNotes.Object, _invoices.Object, _clinics.Object, _patients.Object,
            _pdf.Object, _clinicResolver.Object, NullLogger<GetCreditNotePdfQueryHandler>.Instance);

        var result = await handler.Handle(new GetCreditNotePdfQuery { Id = note.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("introuvable", result.Error);
        _pdf.Verify(p => p.GenerateAvoirPdfAsync(
            It.IsAny<AvoirPdfData>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

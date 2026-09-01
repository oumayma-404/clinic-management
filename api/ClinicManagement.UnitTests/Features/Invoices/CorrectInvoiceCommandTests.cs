using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// Opening a correction on an issued note: a <b>draft copy</b>, pointed at the note it will replace.
///
/// <para><b>What this half must NOT do is most of what it is for.</b> The original keeps its number, its
/// status and its payments until the replacement is issued — voiding up front would take real money out of
/// la caisse for as long as the dentist spends editing, and out of it permanently if they walk away. The
/// destructive half lives in <c>IssueInvoiceCommand</c>; see <see cref="IssueReplacementInvoiceTests"/>.</para>
/// </summary>
public class CorrectInvoiceCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime PaidOn = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private Invoice? _added;

    private CorrectInvoiceCommandHandler CreateHandler() => new(
        _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<CorrectInvoiceCommandHandler>.Instance);

    private static Invoice PaidInvoice(Guid clinicId, decimal total = 180m)
    {
        var invoice = new Invoice(Guid.NewGuid(), clinicId, PatientId, dentalRecordId: RecordId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 2, total / 2m, (Guid?)RecordId) });
        invoice.Issue("2026-0073");
        invoice.RecordPayment(total, PaymentMethod.Cash, PaidOn);
        return invoice;
    }

    private void Arrange(Invoice invoice)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _invoices.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice i, CancellationToken _) => _added = i)
            .ReturnsAsync((Invoice i, CancellationToken _) => i);
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync((Patient?)null);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private Task<Result<Application.DTOs.InvoiceDto>> Correct(Invoice invoice, string reason = "Erreur de tarif") =>
        CreateHandler().Handle(new CorrectInvoiceCommand { Id = invoice.Id, Reason = reason }, CancellationToken.None);

    // The replacement is a DRAFT carrying the original's lines, patient and provenance links.
    [Fact]
    public async Task Correcting_Raises_A_Draft_Copy()
    {
        var original = PaidInvoice(ClinicId);
        Arrange(original);

        var result = await Correct(original);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_added);
        Assert.Equal(InvoiceStatus.Draft, _added!.Status);
        Assert.Null(_added.Number);
        Assert.Equal(original.PatientId, _added.PatientId);
        Assert.Equal(original.DentalRecordId, _added.DentalRecordId);
        Assert.Equal(original.TotalTtc, _added.TotalTtc);
        Assert.Equal(original.Lines.Single().Designation, _added.Lines.Single().Designation);
        Assert.Equal(RecordId, _added.Lines.Single().DentalRecordId);
    }

    // The link and the reason ride on the draft, because the reason is spent at ISSUE time — that is when the
    // predecessor's payments are voided and it is cancelled, and both refuse to happen without one.
    [Fact]
    public async Task The_Draft_Points_At_The_Note_It_Replaces_And_Carries_The_Reason()
    {
        var original = PaidInvoice(ClinicId);
        Arrange(original);

        await Correct(original, "Mauvaise dent");

        Assert.Equal(original.Id, _added!.SupersedesInvoiceId);
        Assert.Equal("Mauvaise dent", _added.SupersedesReason);
    }

    // ⚠️ THE load-bearing assertion of this half. Nothing about the original moves yet: its number, its status
    // and — above all — its money stay exactly where they were, for as long as the correction is being typed.
    [Fact]
    public async Task The_Original_Is_Untouched_Until_The_Replacement_Is_Issued()
    {
        var original = PaidInvoice(ClinicId);
        Arrange(original);

        await Correct(original);

        Assert.Equal(InvoiceStatus.Paid, original.Status);
        Assert.Equal("2026-0073", original.Number);
        Assert.Equal(180m, original.AmountCollected);
        Assert.False(original.Payments.Single().IsVoided);
        Assert.Null(original.SupersededByInvoiceId);
    }

    // A draft is already editable in place; sending it round this loop would spend a number for nothing and
    // leave a cancelled shell behind.
    [Fact]
    public async Task A_Draft_Is_Refused_With_The_Alternative_Named()
    {
        var draft = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        draft.SetLines(new[] { ("Détartrage", 1, 60m) });
        Arrange(draft);

        var result = await Correct(draft);

        Assert.True(result.IsFailure);
        Assert.Contains("modifiez-le directement", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_added);
    }

    // Corrected once; the correction is what gets corrected next.
    [Fact]
    public async Task An_Already_Corrected_Note_Is_Refused_And_Points_At_Its_Replacement()
    {
        var original = PaidInvoice(ClinicId);
        original.MarkSupersededBy(Guid.NewGuid());
        Arrange(original);

        var result = await Correct(original);

        Assert.True(result.IsFailure);
        Assert.Contains("déjà été corrigée", result.Error);
    }

    [Fact]
    public async Task A_Cancelled_Note_Is_Refused()
    {
        var original = PaidInvoice(ClinicId);
        original.VoidPayment(original.Payments.Single().Id, "x", creditedTotal: 0m);
        original.Cancel("Annulée ailleurs");
        Arrange(original);

        var result = await Correct(original);

        Assert.True(result.IsFailure);
        Assert.Contains("annulée", result.Error);
    }

    // The reason ends up on the cancellation and on every voided payment, so it cannot be blank.
    [Fact]
    public async Task A_Blank_Reason_Is_Refused_Before_Anything_Is_Read()
    {
        var original = PaidInvoice(ClinicId);
        Arrange(original);

        var result = await Correct(original, "   ");

        Assert.True(result.IsFailure);
        Assert.Null(_added);
        _invoices.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Tenant isolation: another clinic's note reads as "not found", never as correctable.
    [Fact]
    public async Task Another_Clinics_Note_Is_Not_Found()
    {
        var foreign = PaidInvoice(OtherClinicId);
        Arrange(foreign);

        var result = await Correct(foreign);

        Assert.True(result.IsFailure);
        Assert.Equal("Facture introuvable.", result.Error);
        Assert.Null(_added);
    }
}

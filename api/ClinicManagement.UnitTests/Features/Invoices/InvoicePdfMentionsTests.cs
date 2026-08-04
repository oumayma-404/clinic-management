using ClinicManagement.UnitTests.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [J10] The printed note d'honoraires carries the patient's address, and a clinic with nothing to print still
/// renders a document.
///
/// <para>
/// <c>InvoicePdfData</c> had <b>no patient address at all</b>. It is added because it is useful — a printed note
/// is what a patient files, forwards to an insurer, or attaches to a CNAM claim — and NOT because it is required:
/// Code TVA art. 18 § II demands the client's address only for a client subject to the déclaration d'existence,
/// i.e. a business, never a private patient. That distinction is the reason every test here is about the field
/// being <b>optional</b>: making it a validation blocker would refuse to print a legitimate note.
/// </para>
/// <para>
/// The assertions capture the <see cref="InvoicePdfData"/> handed to the renderer. The footer half of J10 — the
/// unconditional « soumise au timbre fiscal » mention — lives inside the QuestPDF composition and is recorded as
/// a coverage note in <c>progress.md</c>: there is no text extraction in this repo, so asserting the rendered
/// wording is not possible here. What IS pinned below is the datum the gate reads (<c>StampDutyAmount</c>).
/// </para>
/// </summary>
public class InvoicePdfMentionsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPdfGenerationService> _pdf = new();
    private readonly Mock<IQrCodeGenerator> _qr = new();
    private readonly Mock<ICnamBillingCalculator> _cnam = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private InvoicePdfData? _captured;

    public InvoicePdfMentionsTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _cnam.Setup(c => c.ComputeAsync(
                It.IsAny<IReadOnlyCollection<CnamBillingLine>>(), It.IsAny<decimal>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<CnamBillingLine> _, decimal total, DateTime? _, DateTime _, CancellationToken _)
                => new CnamSplit(0m, total));
        _pdf.Setup(p => p.GenerateInvoicePdfAsync(It.IsAny<InvoicePdfData>(), It.IsAny<CancellationToken>()))
            .Callback((InvoicePdfData data, CancellationToken _) => _captured = data)
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    private Invoice IssuedInvoice(bool stampEnabled = true)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });
        invoice.Issue("2026-0001", vatApplicable: true, vatRate: 7m,
            stampDutyEnabled: stampEnabled, stampDutyAmount: stampEnabled ? 1.000m : 0m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    private void PatientHas(Address? address)
    {
        var patient = new Patient(
            PatientId, ClinicId, "Amal", "Ben Salah",
            new DateTime(1990, 4, 3, 0, 0, 0, DateTimeKind.Utc), "Femme",
            address: address);
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
    }

    private async Task<InvoicePdfData> RenderAsync(Invoice invoice)
    {
        var handler = new GetInvoicePdfQueryHandler(
            _invoices.Object, _clinics.Object, _patients.Object, _pdf.Object, _qr.Object, _cnam.Object,
            _clinicResolver.Object, NullLogger<GetInvoicePdfQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoicePdfQuery { Id = invoice.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_captured);
        return _captured!;
    }

    // [J10] The address reaches the render model, on one line, in the order a Tunisian envelope is written.
    [Fact]
    public async Task The_Patient_Address_Reaches_The_Render_Model()
    {
        PatientHas(new Address("12 rue de Marseille", "Tunis", "Tunis", "1000"));
        var invoice = IssuedInvoice();

        var data = await RenderAsync(invoice);

        Assert.Equal("12 rue de Marseille, 1000 Tunis", data.PatientAddress);
    }

    // [J10] The gouvernorat is deliberately omitted — on the address of almost every patient a cabinet sees it
    // duplicates the city, and « Tunis, 1000 Tunis, Tunis » is not an address anyone writes.
    [Fact]
    public async Task The_Governorate_Is_Not_Repeated()
    {
        PatientHas(new Address("5 avenue Habib Bourguiba", "Sfax", "Sfax", "3000"));
        var invoice = IssuedInvoice();

        var data = await RenderAsync(invoice);

        Assert.Equal("5 avenue Habib Bourguiba, 3000 Sfax", data.PatientAddress);
    }

    // [J10] The country is omitted too: a note issued in Tunisia to a Tunisian address does not name the country.
    [Fact]
    public async Task The_Country_Is_Not_Printed()
    {
        PatientHas(new Address("12 rue de Marseille", "Tunis", "Tunis", "1000", "Tunisie"));
        var invoice = IssuedInvoice();

        var data = await RenderAsync(invoice);

        Assert.DoesNotContain("Tunisie", data.PatientAddress);
    }

    // [J10][edge] A patient with NO address still renders — null, never an empty « Adresse : » line the reader has
    // to interpret, and never a refusal. This is the case the "not legally required" reasoning exists to protect.
    [Fact]
    public async Task A_Patient_With_No_Address_Still_Renders()
    {
        PatientHas(null);
        var invoice = IssuedInvoice();

        var data = await RenderAsync(invoice);

        Assert.Null(data.PatientAddress);
        _pdf.Verify(p => p.GenerateInvoicePdfAsync(
            It.IsAny<InvoicePdfData>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // [J10][edge] And a patient the read cannot resolve at all does not throw — the document is the point.
    [Fact]
    public async Task A_Missing_Patient_Still_Renders()
    {
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        var invoice = IssuedInvoice();

        var data = await RenderAsync(invoice);

        Assert.Null(data.PatientAddress);
        Assert.Equal(string.Empty, data.PatientName);
    }

    // [J10][edge] A clinic with no address and no matricule fiscal must still render a document, not throw. The
    // spec names this explicitly, and it is the ordinary state of a clinic on its first day.
    [Fact]
    public async Task A_Clinic_With_No_Address_Or_Matricule_Still_Renders()
    {
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Neuf"));
        PatientHas(null);
        var invoice = IssuedInvoice();

        var data = await RenderAsync(invoice);

        Assert.Equal("Cabinet Neuf", data.ClinicName);
        Assert.Null(data.ClinicAddress);
        Assert.Null(data.MatriculeFiscal);
    }

    // [J10] The datum the footer gate reads. The mention is now conditional on `StampDutyAmount > 0`, so a note
    // issued with the timbre switched off must carry 0 here — otherwise the gate has nothing to switch on and the
    // document would assert a droit de timbre it never charged.
    [Fact]
    public async Task An_Invoice_Issued_Without_The_Timbre_Carries_Zero_Stamp()
    {
        PatientHas(null);
        var invoice = IssuedInvoice(stampEnabled: false);

        var data = await RenderAsync(invoice);

        Assert.Equal(0m, data.StampDutyAmount);
    }

    // [J10] …and the ordinary case still carries the 1,000 DT the tariff prescribes.
    [Fact]
    public async Task An_Invoice_With_The_Timbre_Carries_One_Dinar()
    {
        PatientHas(null);
        var invoice = IssuedInvoice(stampEnabled: true);

        var data = await RenderAsync(invoice);

        Assert.Equal(1.000m, data.StampDutyAmount);
    }
}

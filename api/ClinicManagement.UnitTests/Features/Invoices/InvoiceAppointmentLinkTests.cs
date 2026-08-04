using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// The invoice↔appointment link (audit § 6.8, ACs P6.12–6.14).
///
/// <para><b>What the finding actually was.</b> <c>Invoice.AppointmentId</c> has existed since the invoice was
/// written; the command accepted it, the DTO returned it, the EF configuration mapped it — and <b>nothing ever
/// set it</b>. A column nobody writes is also a column nobody validates, which is why the create path is tested
/// here for tenant and patient agreement alongside the read.</para>
/// </summary>
public class InvoiceAppointmentLinkTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherPatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    // L9 — the attribution dependencies. Deliberately arranged to reproduce this test's ORIGINAL behaviour: an
    // empty roster and no caller doctor means `PractitionerAttribution.Resolve` finds no candidate and the
    // aggregate stays unattributed, exactly as it was before the column existed. Attribution itself is covered by
    // its own tests, not by re-purposing these.
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private static Patient NewPatient(Guid id, Guid clinicId) =>
        new(id, clinicId, "Amal", "Ben Salah", new DateTime(1990, 4, 3), "Femme");

    private static Appointment NewAppointment(Guid clinicId, Guid? patientId) =>
        new(Guid.NewGuid(), clinicId, patientId, doctorId: null,
            appointmentDateTime: new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            duration: TimeSpan.FromMinutes(30), doctorName: "Dr Test", notes: null);

    private CreateInvoiceCommandHandler CreateHandler() => new(
        _invoices.Object, _patients.Object, _appointments.Object, _doctors.Object, _clinicContext.Object,
        _clinicResolver.Object, _uow.Object,
        NullLogger<CreateInvoiceCommandHandler>.Instance);

    private void ArrangeClinicAndPatient()
    {
        _doctors.Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Doctor>());
        _clinicContext.Setup(c => c.GetUserId()).Returns((string?)null);
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPatient(PatientId, ClinicId));
    }

    // ---- Write side (AC-P6.12) ----------------------------------------------

    [Fact]
    public async Task Create_Persists_The_Appointment_Link() // [AC-P6.12]
    {
        ArrangeClinicAndPatient();
        var visit = NewAppointment(ClinicId, PatientId);
        _appointments.Setup(r => r.GetByIdAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(visit);

        Invoice? saved = null;
        _invoices.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice i, CancellationToken _) => saved = i)
            .ReturnsAsync((Invoice i, CancellationToken _) => i);

        var result = await CreateHandler().Handle(
            new CreateInvoiceCommand
            {
                PatientId = PatientId,
                AppointmentId = visit.Id,
                Lines = new() { new InvoiceLineRequest { Designation = "Détartrage", Quantity = 1, UnitPriceHt = 100m } },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(visit.Id, saved!.AppointmentId);
        Assert.Equal(visit.Id, result.Value!.AppointmentId);
    }

    [Fact]
    public async Task Create_Refuses_An_Appointment_From_Another_Clinic() // [AC-P6.12] tenant isolation
    {
        ArrangeClinicAndPatient();
        var foreign = NewAppointment(OtherClinicId, PatientId);
        _appointments.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await CreateHandler().Handle(
            new CreateInvoiceCommand { PatientId = PatientId, AppointmentId = foreign.Id },
            CancellationToken.None);

        // Reads as "not found", like every other cross-clinic id in this codebase — it must not disclose that
        // the appointment exists somewhere else.
        Assert.True(result.IsFailure);
        Assert.Equal("Rendez-vous introuvable.", result.Error);
        _invoices.Verify(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Refuses_An_Appointment_Belonging_To_Another_Patient() // [AC-P6.12]
    {
        ArrangeClinicAndPatient();
        var someoneElses = NewAppointment(ClinicId, OtherPatientId);
        _appointments.Setup(r => r.GetByIdAsync(someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        var result = await CreateHandler().Handle(
            new CreateInvoiceCommand { PatientId = PatientId, AppointmentId = someoneElses.Id },
            CancellationToken.None);

        // Same clinic, so the message can be specific: silently accepting it would put « Facturé » on a visit
        // that belongs to a different patient's record.
        Assert.True(result.IsFailure);
        Assert.Contains("autre patient", result.Error);
    }

    [Fact]
    public async Task Create_Without_An_Appointment_Is_Unchanged() // [AC-P6.14]
    {
        ArrangeClinicAndPatient();

        Invoice? saved = null;
        _invoices.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice i, CancellationToken _) => saved = i)
            .ReturnsAsync((Invoice i, CancellationToken _) => i);

        var result = await CreateHandler().Handle(
            new CreateInvoiceCommand { PatientId = PatientId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(saved!.AppointmentId);
        // The link is optional, so no appointment lookup should happen at all.
        _appointments.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Read side (AC-P6.13) ----------------------------------------------

    [Fact]
    public async Task Resolve_Reports_The_Live_Invoice_For_A_Visit() // [AC-P6.13]
    {
        var visitId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        _invoices.Setup(r => r.GetAppointmentLinksAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (visitId, invoiceId, "2026-0007", InvoiceStatus.Issued),
            });

        var links = await AppointmentInvoiceLinks.ResolveAsync(_invoices.Object, ClinicId, new[] { visitId });

        Assert.Equal(invoiceId, links[visitId].InvoiceId);
        Assert.Equal("2026-0007", links[visitId].Number);
    }

    [Fact]
    public async Task Resolve_Ignores_A_Cancelled_Invoice() // [AC-P6.13]
    {
        var visitId = Guid.NewGuid();
        _invoices.Setup(r => r.GetAppointmentLinksAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (visitId, Guid.NewGuid(), "2026-0008", InvoiceStatus.Cancelled),
            });

        var links = await AppointmentInvoiceLinks.ResolveAsync(_invoices.Object, ClinicId, new[] { visitId });

        // « Facturé » with no money behind it would also hide the action needed to raise a replacement.
        Assert.Empty(links);
    }

    [Fact]
    public async Task Resolve_Prefers_An_Issued_Invoice_Over_A_Stray_Draft() // [AC-P6.13]
    {
        var visitId = Guid.NewGuid();
        var issuedId = Guid.NewGuid();
        _invoices.Setup(r => r.GetAppointmentLinksAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (visitId, Guid.NewGuid(), null, InvoiceStatus.Draft),
                (visitId, issuedId, "2026-0009", InvoiceStatus.Issued),
            });

        var links = await AppointmentInvoiceLinks.ResolveAsync(_invoices.Object, ClinicId, new[] { visitId });

        // The link is a soft one — nothing in the schema stops two invoices pointing at one visit — so the badge
        // must name the number the patient was actually given.
        Assert.Equal(issuedId, links[visitId].InvoiceId);
        Assert.Equal("2026-0009", links[visitId].Number);
    }

    [Fact]
    public async Task Resolve_Reads_Nothing_For_An_Empty_Page()
    {
        var links = await AppointmentInvoiceLinks.ResolveAsync(_invoices.Object, ClinicId, Array.Empty<Guid>());

        Assert.Empty(links);
        _invoices.Verify(r => r.GetAppointmentLinksAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Appointments_List_Exposes_The_Link_Per_Row() // [AC-P6.13]
    {
        var billed = NewAppointment(ClinicId, PatientId);
        var unbilled = NewAppointment(ClinicId, PatientId);
        var invoiceId = Guid.NewGuid();

        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        _appointments.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { billed, unbilled });
        _invoices.Setup(r => r.GetAppointmentLinksAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (billed.Id, invoiceId, "2026-0010", InvoiceStatus.Paid),
            });

        var handler = new GetAppointmentsQueryHandler(
            _appointments.Object, _invoices.Object, users.Object, context.Object);
        var result = await handler.Handle(new GetAppointmentsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rows = result.Value!.ToList();
        Assert.Equal("2026-0010", rows.Single(a => a.Id == billed.Id).InvoiceNumber);
        Assert.Equal(invoiceId, rows.Single(a => a.Id == billed.Id).InvoiceId);
        Assert.Null(rows.Single(a => a.Id == unbilled.Id).InvoiceId);

        // One batched read for the whole page, not one per row — and bounded by the ids in the window.
        _invoices.Verify(r => r.GetAppointmentLinksAsync(
            ClinicId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

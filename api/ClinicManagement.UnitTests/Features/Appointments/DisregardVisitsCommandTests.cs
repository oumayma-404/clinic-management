using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « Retirer de la liste » / « Supprimer (créé par erreur) » — the exit that asserts nothing, and the one floor
/// under it.
///
/// <para>The floor is the point of this class. The mark takes a séance out of the dashboard's figures, so applied
/// to a visit that produced a fiche and a paid note it does not tidy anything up: the visit leaves « Rendez-vous
/// par statut » and the taux d'absence while its money stays in la caisse, and nothing records why the two halves
/// of the same day disagree. Everything below is one half of « which séances may leave, and which may not ».</para>
/// </summary>
public class DisregardVisitsCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string UserId = "local|11111111-1111-1111-1111-111111111111";

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalRecordRepository> _dentalRecords = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    // No links unless a case says so. Moq answers an un-stubbed Task-returning method with a null result, and the
    // handler's catch-all would turn that NullReferenceException into a plain Failure — every case below would
    // « fail » for the same reason and say nothing about the guard.
    public DisregardVisitsCommandTests()
    {
        _dentalRecords
            .Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, decimal)>());

        _invoices
            .Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
    }

    private static Appointment AppointmentIn(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, Guid.NewGuid(), doctorId: null,
            new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

    private DisregardVisitsCommandHandler Handler()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinicContext.Setup(c => c.GetUserId()).Returns(UserId);

        return new DisregardVisitsCommandHandler(
            _appointments.Object, _dentalRecords.Object, _invoices.Object, _unitOfWork.Object,
            _clinicResolver.Object, _clinicContext.Object,
            NullLogger<DisregardVisitsCommandHandler>.Instance);
    }

    private void Existing(Appointment appointment) =>
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);

    private void HasFiche(Appointment appointment) =>
        _dentalRecords
            .Setup(r => r.GetAppointmentLinksAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (appointment.Id, Guid.NewGuid(), 120m) });

    private void HasInvoice(Appointment appointment, InvoiceStatus status) =>
        _invoices
            .Setup(r => r.GetAppointmentLinksAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (appointment.Id, Guid.NewGuid(), (string?)"2026-0042", status) });

    private Task<Result<DisregardVisitsResultDto>> Retire(params Guid[] ids) =>
        Handler().Handle(
            new DisregardVisitsCommand { AppointmentIds = ids.ToList(), Disregard = true },
            CancellationToken.None);

    [Fact]
    public async Task A_Bare_Séance_Is_Retired_And_Records_Who_Did_It()
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);

        var result = await Retire(appointment.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Changed);
        Assert.Empty(result.Value.Refused);
        Assert.True(appointment.IsDisregarded);
        Assert.Equal(UserId, appointment.DisregardedByUserId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Séance_With_A_Fiche_Is_Refused()
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);
        HasFiche(appointment);

        var result = await Retire(appointment.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Changed);
        Assert.Equal(new[] { appointment.Id }, result.Value.Refused);
        Assert.False(appointment.IsDisregarded);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Séance_With_A_Live_Note_DHonoraires_Is_Refused()
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);
        HasInvoice(appointment, InvoiceStatus.Issued);

        var result = await Retire(appointment.Id);

        Assert.Equal(new[] { appointment.Id }, result.Value!.Refused);
        Assert.False(appointment.IsDisregarded);
    }

    // A cancelled note bills nothing, and a séance whose only note was voided is exactly the mis-booking this
    // exists for. Which note counts is `AppointmentInvoiceLinks`' rule, not a second copy of it here.
    [Fact]
    public async Task A_Séance_Whose_Only_Note_Is_Cancelled_Is_Retired()
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);
        HasInvoice(appointment, InvoiceStatus.Cancelled);

        var result = await Retire(appointment.Id);

        Assert.Equal(1, result.Value!.Changed);
        Assert.Empty(result.Value.Refused);
        Assert.True(appointment.IsDisregarded);
    }

    // The guard stops a séance LEAVING the figures; it must never strand one outside them. A row put back while it
    // carries a fiche is a row returning to the count, which is always the safe direction.
    [Fact]
    public async Task Putting_A_Séance_With_A_Fiche_Back_Is_Not_Guarded()
    {
        var appointment = AppointmentIn(ClinicId);
        appointment.Disregard(UserId, DateTime.UtcNow);
        Existing(appointment);
        HasFiche(appointment);

        var result = await Handler().Handle(
            new DisregardVisitsCommand
            {
                AppointmentIds = new List<Guid> { appointment.Id }, Disregard = false,
            },
            CancellationToken.None);

        Assert.Equal(1, result.Value!.Changed);
        Assert.Empty(result.Value.Refused);
        Assert.False(appointment.IsDisregarded);
        _dentalRecords.Verify(
            r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // One billed row must not fail the whole selection: undoing a calendar import is a hundred rows at a time, and
    // an all-or-nothing refusal would make the honest exit unusable exactly where it is needed.
    [Fact]
    public async Task A_Refused_Row_Does_Not_Stop_The_Rest_Of_The_Selection()
    {
        var billed = AppointmentIn(ClinicId);
        var phantom = AppointmentIn(ClinicId);
        Existing(billed);
        Existing(phantom);
        HasFiche(billed);

        var result = await Retire(billed.Id, phantom.Id);

        Assert.Equal(1, result.Value!.Changed);
        Assert.Equal(new[] { billed.Id }, result.Value.Refused);
        Assert.False(billed.IsDisregarded);
        Assert.True(phantom.IsDisregarded);
    }

    // Same outcome for « does not exist » and « belongs to another clinic », and neither is a refusal: a Refused id
    // tells the caller the séance is real and carries work, which is a fact about another tenant's data.
    [Fact]
    public async Task Another_Clinics_Séance_Is_Skipped_Not_Refused()
    {
        var appointment = AppointmentIn(OtherClinicId);
        Existing(appointment);
        HasFiche(appointment);

        var result = await Retire(appointment.Id);

        Assert.Equal(new[] { appointment.Id }, result.Value!.Skipped);
        Assert.Empty(result.Value.Refused);
        Assert.False(appointment.IsDisregarded);
    }
}

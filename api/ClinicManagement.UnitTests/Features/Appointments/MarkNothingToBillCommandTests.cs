using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « Rien à facturer » — the closure worklist's escape hatch, and the only part of it a human types.
/// </summary>
public class MarkNothingToBillCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string UserId = "local|11111111-1111-1111-1111-111111111111";

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private static Appointment AppointmentIn(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, Guid.NewGuid(), doctorId: null,
            new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

    private MarkNothingToBillCommandHandler Handler()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinicContext.Setup(c => c.GetUserId()).Returns(UserId);

        return new MarkNothingToBillCommandHandler(
            _appointments.Object, _unitOfWork.Object, _clinicResolver.Object, _clinicContext.Object,
            NullLogger<MarkNothingToBillCommandHandler>.Instance);
    }

    private void Existing(Appointment appointment) =>
        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);

    // The whole value of the mark is that « pourquoi cette séance n'a produit aucun document ? » stays answerable
    // months later, and a blank motif answers nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Marking_Without_A_Motif_Is_Refused(string? reason)
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);

        var result = await Handler().Handle(
            new MarkNothingToBillCommand { AppointmentId = appointment.Id, NothingToBill = true, Reason = reason },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(appointment.IsNothingToBill);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Marking_Records_The_Motif_The_Actor_And_The_Moment()
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);

        var result = await Handler().Handle(
            new MarkNothingToBillCommand
            {
                AppointmentId = appointment.Id, NothingToBill = true, Reason = "  Contrôle offert  ",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.True(appointment.IsNothingToBill);
        Assert.Equal("Contrôle offert", appointment.NothingToBillReason);
        Assert.Equal(UserId, appointment.NothingToBillByUserId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // A second caller is a double-click far more often than a considered change of mind, and overwriting would
    // erase a colleague's reasoning with no trace of it anywhere.
    [Fact]
    public async Task Re_Marking_Keeps_The_First_Motif_And_The_First_Author()
    {
        var appointment = AppointmentIn(ClinicId);
        Existing(appointment);
        var handler = Handler();

        await handler.Handle(
            new MarkNothingToBillCommand { AppointmentId = appointment.Id, NothingToBill = true, Reason = "Premier" },
            CancellationToken.None);
        await handler.Handle(
            new MarkNothingToBillCommand { AppointmentId = appointment.Id, NothingToBill = true, Reason = "Second" },
            CancellationToken.None);

        Assert.Equal("Premier", appointment.NothingToBillReason);
    }

    // The mark is a claim about money that can turn out to be wrong; a claim nobody can withdraw is one people
    // stop making. Nothing clinical is destroyed either way.
    [Fact]
    public async Task Withdrawing_Clears_The_Mark_And_Needs_No_Motif()
    {
        var appointment = AppointmentIn(ClinicId);
        appointment.MarkNothingToBill("Contrôle offert", UserId, DateTime.UtcNow);
        Existing(appointment);

        var result = await Handler().Handle(
            new MarkNothingToBillCommand { AppointmentId = appointment.Id, NothingToBill = false },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.False(appointment.IsNothingToBill);
        Assert.Null(appointment.NothingToBillReason);
    }

    // One refusal for « does not exist » and « belongs to another clinic »: telling the two apart would confirm
    // that an id exists somewhere in the deployment.
    [Fact]
    public async Task Another_Clinics_Appointment_Is_Refused_And_Untouched()
    {
        var foreign = AppointmentIn(OtherClinicId);
        Existing(foreign);

        var result = await Handler().Handle(
            new MarkNothingToBillCommand { AppointmentId = foreign.Id, NothingToBill = true, Reason = "Motif" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(foreign.IsNothingToBill);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_Appointment_Is_Refused()
    {
        _appointments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await Handler().Handle(
            new MarkNothingToBillCommand { AppointmentId = Guid.NewGuid(), NothingToBill = true, Reason = "Motif" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}

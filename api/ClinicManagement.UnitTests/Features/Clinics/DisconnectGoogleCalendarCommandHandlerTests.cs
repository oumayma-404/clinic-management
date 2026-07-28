using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// [AC-P2.33–2.35] Disconnecting a clinic's Google Calendar. <c>Clinic.ClearGoogleCalendarConnection()</c> had
/// existed with zero callers since Google tokens moved per-clinic into the DB, so a clinic that authorised the
/// wrong Google account could only overwrite it by re-running the whole OAuth flow — never simply stop syncing.
/// </summary>
public class DisconnectGoogleCalendarCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private DisconnectGoogleCalendarCommandHandler Handler() =>
        new(_clinics.Object, _users.Object, _context.Object, _uow.Object,
            NullLogger<DisconnectGoogleCalendarCommandHandler>.Instance);

    private static Clinic NewClinic(bool connected)
    {
        var clinic = new Clinic(ClinicId, "Cabinet Test", code: "CODE01", city: "Tunis");
        if (connected)
        {
            clinic.SetGoogleCalendarConnection("refresh-token-value", "clinic@example.com");
        }
        return clinic;
    }

    private void AsCaller(User caller)
    {
        _context.Setup(c => c.GetUserId()).Returns(caller.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    // [AC-P2.33] The refresh token and calendar id are both cleared, so status reports « non connecté » and
    // App→Google pushes stop.
    [Fact]
    public async Task Handle_Clears_The_Connection()
    {
        var admin = Local(User.RoleAdmin);
        var clinic = NewClinic(connected: true);
        AsCaller(admin);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);

        var result = await Handler().Handle(new DisconnectGoogleCalendarCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(clinic.GoogleRefreshToken);
        Assert.Null(clinic.GoogleCalendarId);
        _clinics.Verify(r => r.UpdateAsync(clinic, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Idempotent: reconnecting and disconnecting twice while fixing a wrong account is normal admin behaviour,
    // so "nothing connected" is a successful no-op rather than an error, and writes nothing.
    [Fact]
    public async Task Handle_Is_A_No_Op_When_Nothing_Is_Connected()
    {
        var admin = Local(User.RoleAdmin);
        var clinic = NewClinic(connected: false);
        AsCaller(admin);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);

        var result = await Handler().Handle(new DisconnectGoogleCalendarCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _clinics.Verify(r => r.UpdateAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-P2.34] AdminOnly at the endpoint, and re-checked here against the DB role — the authoritative source.
    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task Handle_Rejects_A_Non_Admin(string role)
    {
        var caller = Local(role);
        var clinic = NewClinic(connected: true);
        AsCaller(caller);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);

        var result = await Handler().Handle(new DisconnectGoogleCalendarCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("refresh-token-value", clinic.GoogleRefreshToken);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Failures are French and leak nothing (the § 2 sweep's standing rule).
    [Fact]
    public async Task Handle_Fails_In_French_When_The_Clinic_Is_Missing()
    {
        var admin = Local(User.RoleAdmin);
        AsCaller(admin);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);

        var result = await Handler().Handle(new DisconnectGoogleCalendarCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cabinet introuvable.", result.Error);
    }
}

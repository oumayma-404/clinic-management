using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Queries;
using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// « Mode discret » (<c>features/money-discreet-mode</c>): the two toggles, the step-up that refuses a password for
/// « Afficher », and the dashboard read that stops reading money while the cabinet is hidden.
/// </summary>
public class ClinicMoneyMaskTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static User Local(string role, bool withAuthenticator)
    {
        var user = User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");
        if (withAuthenticator)
        {
            user.IssueTotpSecret("PROTECTED");
            user.CompleteTotpEnrolment(new[] { "recovery-1" });
        }

        return user;
    }

    private Clinic SignedIn(User user, bool moneyHidden = false)
    {
        var clinic = new Clinic(ClinicId, "Clinic", code: "ABC123");
        if (moneyHidden)
        {
            clinic.HideMoney();
        }

        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return clinic;
    }

    private HideMoneyCommandHandler Hide() => new(_clinics.Object, _users.Object, _context.Object, _uow.Object);

    private ShowMoneyCommandHandler Show() => new(_clinics.Object, _users.Object, _context.Object, _uow.Object);

    // ---- hide ---------------------------------------------------------------------------------------------

    // [AC-1] One click, no code: the cabinet is hidden and the change is saved.
    [Fact]
    public async Task An_Admin_With_An_Authenticator_Hides_The_Money()
    {
        var clinic = SignedIn(Local("admin", withAuthenticator: true));

        var result = await Hide().Handle(new HideMoneyCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsMoneyHidden);
        Assert.True(clinic.IsMoneyHidden);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-7] A cabinet must never hide what it cannot bring back.
    [Fact]
    public async Task An_Admin_Without_An_Authenticator_Cannot_Hide()
    {
        var clinic = SignedIn(Local("admin", withAuthenticator: false));

        var result = await Hide().Handle(new HideMoneyCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TotpNotEnrolled, result.Code);
        Assert.False(clinic.IsMoneyHidden);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-6] Behind the AdminOnly policy, the handler refuses too.
    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task A_Non_Admin_Cannot_Hide(string role)
    {
        var clinic = SignedIn(Local(role, withAuthenticator: true));

        var result = await Hide().Handle(new HideMoneyCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(clinic.IsMoneyHidden);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A double click writes nothing the second time — no audit row, no broadcast noise.
    [Fact]
    public async Task Hiding_An_Already_Hidden_Cabinet_Writes_Nothing()
    {
        SignedIn(Local("admin", withAuthenticator: true), moneyHidden: true);

        var result = await Hide().Handle(new HideMoneyCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsMoneyHidden);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- show ---------------------------------------------------------------------------------------------

    // [AC-4] Shown, and saved — so it stays shown after a reload and on other PCs.
    [Fact]
    public async Task An_Admin_Shows_The_Money_Again()
    {
        var clinic = SignedIn(Local("admin", withAuthenticator: true), moneyHidden: true);

        var result = await Show().Handle(new ShowMoneyCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsMoneyHidden);
        Assert.False(clinic.IsMoneyHidden);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Non_Admin_Cannot_Show()
    {
        var clinic = SignedIn(Local("doctor", withAuthenticator: true), moneyHidden: true);

        var result = await Show().Handle(new ShowMoneyCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(clinic.IsMoneyHidden);
    }

    // ---- the step-up « Afficher » spends ------------------------------------------------------------------

    private sealed class StepUpHarness
    {
        public readonly Mock<IClinicContext> Context = new();
        public readonly Mock<IUserRepository> Users = new();
        public readonly Mock<ILocalAuthService> Passwords = new();
        public readonly Mock<ITotpService> Totp = new();
        public readonly Mock<IUserSecretProtector> Protector = new();
        public readonly Mock<IStepUpConfirmations> Confirmations = new();
        public readonly Mock<ITotpReplayGuard> Replay = new();

        public StepUpHarness(User user)
        {
            var secret = "SECRET";
            Context.Setup(c => c.GetUserId()).Returns(user.Id);
            Users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            Passwords.Setup(p => p.VerifyPassword(It.IsAny<string>(), "right-password"))
                .Returns(PasswordVerificationOutcome.Success);
            Protector.Setup(p => p.TryUnprotect(It.IsAny<string>(), out secret)).Returns(true);
            Totp.Setup(t => t.VerifyCode("SECRET", "123456")).Returns(true);
            Replay.Setup(r => r.TryConsume(user.Id, It.IsAny<string>())).Returns(true);
            Confirmations.Setup(c => c.Issue(user.Id, It.IsAny<string>())).Returns("TOKEN");
        }

        public StepUpCommandHandler Handler() => new(
            Context.Object, Users.Object, Passwords.Object, Totp.Object, Protector.Object,
            Confirmations.Object, Replay.Object);
    }

    // [AC-5] A correct password does not confirm « Afficher l'argent ».
    [Fact]
    public async Task A_Password_Does_Not_Confirm_Show_Money()
    {
        var harness = new StepUpHarness(Local("admin", withAuthenticator: true));

        var result = await harness.Handler().Handle(
            new StepUpCommand { Action = ClinicMoneyMask.ShowStepUpAction, Password = "right-password" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        harness.Confirmations.Verify(c => c.Issue(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task An_Authenticator_Code_Confirms_Show_Money()
    {
        var harness = new StepUpHarness(Local("admin", withAuthenticator: true));

        var result = await harness.Handler().Handle(
            new StepUpCommand { Action = ClinicMoneyMask.ShowStepUpAction, TotpCode = "123456" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        harness.Confirmations.Verify(c => c.Issue(It.IsAny<string>(), ClinicMoneyMask.ShowStepUpAction), Times.Once);
    }

    // Edge case: the authenticator was removed while hidden — the refusal names it rather than « mauvais code ».
    [Fact]
    public async Task Show_Money_Without_An_Authenticator_Says_So()
    {
        var harness = new StepUpHarness(Local("admin", withAuthenticator: false));

        var result = await harness.Handler().Handle(
            new StepUpCommand { Action = ClinicMoneyMask.ShowStepUpAction, Password = "right-password" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TotpNotEnrolled, result.Code);
        harness.Confirmations.Verify(c => c.RecordFailureAndCheckExhausted(It.IsAny<string>()), Times.Never);
    }

    // Every other action is unchanged: a password still confirms an archive download.
    [Fact]
    public async Task A_Password_Still_Confirms_Every_Other_Action()
    {
        var harness = new StepUpHarness(Local("admin", withAuthenticator: true));

        var result = await harness.Handler().Handle(
            new StepUpCommand { Action = "archive-download", Password = "right-password" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ---- the dashboard -----------------------------------------------------------------------------------

    // [AC-3] Hidden: no money figure on the wire, and the money readers are never asked.
    [Fact]
    public async Task The_Dashboard_Reads_No_Money_While_Hidden()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinics.Setup(r => r.IsMoneyHiddenAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var activity = new Mock<IDashboardActivityReader>();
        activity.Setup(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardActivityDto());
        var alerts = new Mock<IDashboardAlertsReader>();
        alerts.Setup(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardAlertsDto());
        var mix = new Mock<IDashboardProcedureMixReader>();
        mix.Setup(r => r.ReadAsync(
                It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProcedureMixPointDto>());
        var appointmentTrend = new Mock<IDashboardAppointmentTrendReader>();
        appointmentTrend.Setup(r => r.ReadAsync(
                It.IsAny<Guid>(), It.IsAny<DashboardPeriod>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyAppointmentPointDto>());
        var money = new Mock<IDashboardMoneyReader>(MockBehavior.Strict);
        var trend = new Mock<IDashboardTrendReader>(MockBehavior.Strict);

        var handler = new GetDashboardQueryHandler(
            activity.Object, money.Object, alerts.Object, trend.Object, mix.Object, appointmentTrend.Object,
            resolver.Object, _clinics.Object, NullLogger<GetDashboardQueryHandler>.Instance);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Money);
        Assert.Null(result.Value.Receivables);
        Assert.Empty(result.Value.Trend);
        Assert.NotNull(result.Value.Activity);
        Assert.NotNull(result.Value.Alerts);
    }
}

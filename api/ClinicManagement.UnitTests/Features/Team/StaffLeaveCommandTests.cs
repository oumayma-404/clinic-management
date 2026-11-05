using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// A secretary asks, an admin answers, either withdraws: who may do each, what is refused by name, what is stored,
/// and who is told — one admin-role row for a request, the secretary herself for an answer, and an e-mail that can
/// never fail the decision it reports.
/// </summary>
public class StaffLeaveCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly Mon16Nov = new(2026, 11, 16);
    private static readonly DateOnly Wed18Nov = new(2026, 11, 18);
    private static readonly DateOnly Sun22Nov = new(2026, 11, 22);

    private readonly Mock<IStaffLeaveRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<INotificationGenerator> _notifications = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IPublicAppUrlProvider> _appUrl = new();
    private readonly List<User> _roster = new();
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");

    private readonly User _ines;
    private readonly User _admin;

    public StaffLeaveCommandTests()
    {
        _clinic.SetLeaveRequestsEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_roster.ToList()));
        _email.SetupGet(e => e.IsConfigured).Returns(true);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent);
        _appUrl.SetupGet(a => a.IsConfigured).Returns(true);
        _appUrl.SetupGet(a => a.BaseUrl).Returns("https://app.apexa.tn");

        _ines = Account(User.RoleSecretary, "Ines Gharbi");
        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
    }

    private User Account(string role, string name, Guid? clinicId = null)
    {
        var user = User.CreateLocalUser(clinicId ?? ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        if (clinicId is null || clinicId == ClinicId) _roster.Add(user);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private void SignIn(User user) => _context.Setup(c => c.GetUserId()).Returns(user.Id);

    private StaffLeaveRequest Stored(
        User owner, DateOnly start, DateOnly end, StaffLeaveStatus status = StaffLeaveStatus.Pending, Guid? clinicId = null)
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), clinicId ?? ClinicId, owner.Id, StaffLeaveType.PaidLeave, start, end, false, "Mariage", 3m);
        if (status == StaffLeaveStatus.Approved) leave.Approve(_admin.Id, 3m);
        if (status == StaffLeaveStatus.Rejected) leave.Reject(_admin.Id, "Non");
        _requests.Setup(r => r.GetByIdAsync(leave.Id, It.IsAny<CancellationToken>())).ReturnsAsync(leave);
        return leave;
    }

    private StaffLeaveRequest Recorded(User owner, DateOnly start, DateOnly end)
    {
        var leave = StaffLeaveRequest.RecordByAdmin(
            Guid.NewGuid(), ClinicId, owner.Id, StaffLeaveType.Unpaid, start, end, false, "Absence imposée", 3m, _admin.Id);
        _requests.Setup(r => r.GetByIdAsync(leave.Id, It.IsAny<CancellationToken>())).ReturnsAsync(leave);
        return leave;
    }

    private static DateOnly Today => StaffLeaveRules.ClinicToday();

    private SubmitStaffLeaveRequestCommandHandler Submit() => new(
        _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object, _notifications.Object,
        NullLogger<SubmitStaffLeaveRequestCommandHandler>.Instance);

    private DecideStaffLeaveRequestCommandHandler Decide() => new(
        _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object, _notifications.Object,
        _email.Object, _appUrl.Object, NullLogger<DecideStaffLeaveRequestCommandHandler>.Instance);

    private CancelStaffLeaveRequestCommandHandler Cancel() => new(
        _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object, _notifications.Object,
        NullLogger<CancelStaffLeaveRequestCommandHandler>.Instance);

    private RecordStaffAbsenceCommandHandler RecordAbsence() => new(
        _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object, _notifications.Object,
        NullLogger<RecordStaffAbsenceCommandHandler>.Instance);

    private SetLeaveAllowanceCommandHandler SetAllowance() =>
        new(_requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private static SubmitStaffLeaveRequestCommand Ask(DateOnly? start = null, DateOnly? end = null, string? type = "PaidLeave") => new()
    {
        Type = type,
        StartDate = start ?? Mon16Nov,
        EndDate = end ?? Wed18Nov,
        Reason = "Mariage de ma sœur",
    };

    private void VerifyNoWrite()
    {
        _requests.Verify(r => r.AddAsync(It.IsAny<StaffLeaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }


    [Fact]
    public async Task A_Secretary_Asks_And_The_Request_Holds_The_Cabinets_Day_Count()
    {
        _clinic.SetWorkingHours(
            "[" + string.Join(",", Enum.GetValues<DayOfWeek>().Select(d =>
                $"{{\"Day\":\"{d}\",\"Enabled\":{(d is DayOfWeek.Saturday or DayOfWeek.Sunday ? "false" : "true")},\"From\":\"09:00\",\"To\":\"17:00\",\"BreakFrom\":null,\"BreakTo\":null}}")) + "]");
        SignIn(_ines);

        var result = await Submit().Handle(Ask(Mon16Nov, Sun22Nov), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(5m, result.Value!.RequestedDays);
        Assert.Equal("Pending", result.Value.Status);
        _requests.Verify(r => r.AddAsync(It.Is<StaffLeaveRequest>(l =>
            l.UserId == _ines.Id && l.ClinicId == ClinicId && l.RequestedDays == 5m), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Admins_Are_Told_Through_One_Row_With_No_Target()
    {
        SignIn(_ines);

        await Submit().Handle(Ask(), default);

        _notifications.Verify(n => n.StaffLeaveAsync(
            ClinicId, NotificationCategory.StaffLeaveRequested, "Demande de congé",
            It.Is<string>(m => m.StartsWith("Ines Gharbi demande un congé") && m.Contains("(3 jours)")),
            _ines.Id, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(User.RoleAdmin)]
    [InlineData(User.RoleDoctor)]
    public async Task Only_A_Secretary_May_Ask(string role)
    {
        SignIn(Account(role, $"Compte {role}"));

        var result = await Submit().Handle(Ask(), default);

        Assert.Equal(StaffLeaveRules.SecretaryOnly, result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task Nothing_Is_Accepted_While_The_Module_Is_Off()
    {
        _clinic.SetLeaveRequestsEnabled(false);
        SignIn(_ines);

        var result = await Submit().Handle(Ask(), default);

        Assert.Equal(StaffLeaveRules.ModuleOff, result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task An_Unknown_Type_Is_Refused()
    {
        SignIn(_ines);

        var result = await Submit().Handle(Ask(type: "Vacances"), default);

        Assert.Equal(StaffLeaveRules.UnknownType, result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task An_Inverted_Range_Is_Refused_On_Arrival()
    {
        SignIn(_ines);

        var result = await Submit().Handle(Ask(Wed18Nov, Mon16Nov), default);

        Assert.Equal("Le dernier jour doit être le même jour ou après le premier.", result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task An_Overlap_Is_Refused_Naming_The_Other_Request()
    {
        SignIn(_ines);
        var existing = Stored(_ines, Mon16Nov, Wed18Nov);
        _requests.Setup(r => r.FindHoldingOverlapAsync(ClinicId, _ines.Id, Mon16Nov.AddDays(1), Mon16Nov.AddDays(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await Submit().Handle(Ask(Mon16Nov.AddDays(1), Mon16Nov.AddDays(1)), default);

        Assert.Equal("Vous avez déjà une demande du 16/11 au 18/11.", result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task A_Failed_Notification_Never_Fails_The_Request()
    {
        SignIn(_ines);
        _notifications.Setup(n => n.StaffLeaveAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bell down"));

        var result = await Submit().Handle(Ask(), default);

        Assert.True(result.IsSuccess, result.Error);
    }


    [Fact]
    public async Task Accepting_Stores_The_Days_Counted_Under_The_Version_Read()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true, CountedDays = 2.5m, Version = 7 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(StaffLeaveStatus.Approved, leave.Status);
        Assert.Equal(2.5m, leave.CountedDays);
        Assert.Equal("Dr Salma Ben Youssef", result.Value!.DecidedByName);
        _uow.Verify(u => u.SetExpectedVersion(leave, 7u), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accepting_Without_A_Count_Keeps_The_Requested_Days()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true }, default);

        Assert.Equal(3m, leave.CountedDays);
    }

    [Fact]
    public async Task The_Secretary_Is_Told_And_Mailed_When_Accepted()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true, CountedDays = 2.5m }, default);

        _notifications.Verify(n => n.StaffLeaveAsync(
            ClinicId, NotificationCategory.StaffLeaveApproved, "Congé accepté",
            It.Is<string>(m => m.Contains("du 16/11 au 18/11") && m.Contains("2,5 jours")),
            _admin.Id, _ines.Id, It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendAsync(_ines.Email!, "Congé accepté : du 16/11 au 18/11", It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refusing_Requires_A_Motif()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = false, RejectionReason = "  " }, default);

        Assert.Equal("Indiquez le motif du refus.", result.Error);
        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Refusal_Quotes_The_Motif_Word_For_Word()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = false, RejectionReason = "Période chargée" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Période chargée", leave.RejectionReason);
        _notifications.Verify(n => n.StaffLeaveAsync(
            ClinicId, NotificationCategory.StaffLeaveRejected, "Congé refusé",
            It.Is<string>(m => m.Contains("Période chargée") && m.Contains("(3 jours)")), _admin.Id, _ines.Id, It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendAsync(_ines.Email!, "Congé refusé : du 16/11 au 18/11", It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(-1)]
    public async Task A_Count_Outside_Half_Days_Is_Refused(double counted)
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true, CountedDays = (decimal)counted }, default);

        Assert.True(result.IsFailure);
        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);
    }

    [Fact]
    public async Task Only_An_Admin_Decides()
    {
        SignIn(_ines);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true }, default);

        Assert.Equal(StaffLeaveRules.AdminOnly, result.Error);
        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);
    }

    [Fact]
    public async Task A_Request_Already_Answered_Is_Refused_By_Name()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov, StaffLeaveStatus.Rejected);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true }, default);

        Assert.Equal(StaffLeaveRules.NotPending, result.Error);
        _notifications.Verify(n => n.StaffLeaveAsync(
            It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Request_Of_Another_Clinic_Reads_As_Not_Found()
    {
        SignIn(_admin);
        var foreign = Stored(_ines, Mon16Nov, Wed18Nov, clinicId: OtherClinicId);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = foreign.Id, Approve = true }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
        Assert.Equal(StaffLeaveStatus.Pending, foreign.Status);
    }

    [Fact]
    public async Task No_Mail_Is_Attempted_When_The_Sender_Is_Not_Configured()
    {
        SignIn(_admin);
        _email.SetupGet(e => e.IsConfigured).Returns(false);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true }, default);

        Assert.True(result.IsSuccess, result.Error);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Failed_Mail_Never_Fails_The_Decision()
    {
        SignIn(_admin);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(StaffLeaveStatus.Approved, leave.Status);
    }


    [Fact]
    public async Task Withdrawing_A_Waiting_Request_Tells_Nobody()
    {
        SignIn(_ines);
        var leave = Stored(_ines, Today.AddDays(10), Today.AddDays(12));

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id, Version = 3 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(StaffLeaveStatus.Cancelled, leave.Status);
        _uow.Verify(u => u.SetExpectedVersion(leave, 3u), Times.Once);
        _notifications.Verify(n => n.StaffLeaveAsync(
            It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Withdrawing_An_Accepted_Congé_Tells_The_Admins()
    {
        SignIn(_ines);
        var leave = Stored(_ines, Today.AddDays(10), Today.AddDays(12), StaffLeaveStatus.Approved);

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        _notifications.Verify(n => n.StaffLeaveAsync(
            ClinicId, NotificationCategory.StaffLeaveCancelled, "Congé annulé",
            It.Is<string>(m => m.StartsWith("Ines Gharbi a annulé son congé")), _ines.Id, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Admin_Withdrawing_Her_Congé_Tells_Her()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Today.AddDays(10), Today.AddDays(12), StaffLeaveStatus.Approved);

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        _notifications.Verify(n => n.StaffLeaveAsync(
            ClinicId, NotificationCategory.StaffLeaveCancelled, "Congé annulé",
            It.Is<string>(m => m.Contains("a été annulé par Dr Salma Ben Youssef")), _admin.Id, _ines.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Congé_That_Has_Started_Cannot_Be_Withdrawn()
    {
        SignIn(_ines);
        var leave = Stored(_ines, Today, Today.AddDays(2), StaffLeaveStatus.Approved);

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id }, default);

        Assert.Equal(StaffLeaveRules.AlreadyStarted, result.Error);
        Assert.Equal(StaffLeaveStatus.Approved, leave.Status);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Refused_Request_Cannot_Be_Withdrawn()
    {
        SignIn(_ines);
        var leave = Stored(_ines, Today.AddDays(10), Today.AddDays(12), StaffLeaveStatus.Rejected);

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id }, default);

        Assert.Equal(StaffLeaveRules.NotPending, result.Error);
    }

    [Fact]
    public async Task A_Withdrawn_Request_Is_Refused_As_Withdrawn()
    {
        SignIn(_ines);
        var leave = Stored(_ines, Today.AddDays(10), Today.AddDays(12));
        leave.Cancel(_ines.Id, Today);

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id }, default);

        Assert.Equal(StaffLeaveRules.AlreadyCancelled, result.Error);
    }

    [Fact]
    public async Task She_Cannot_Withdraw_An_Absence_The_Admin_Recorded()
    {
        SignIn(_ines);
        var absence = Recorded(_ines, Today.AddDays(10), Today.AddDays(12));

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = absence.Id }, default);

        Assert.Equal(StaffLeaveRules.AdminEnteredOnly, result.Error);
        Assert.Equal(StaffLeaveStatus.Approved, absence.Status);
        Assert.False(StaffLeaveRules.ToDto(absence, new Dictionary<string, User>(), Today, viewerIsAdmin: false).CanCancel);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Admin_Withdraws_An_Absence_She_Recorded()
    {
        SignIn(_admin);
        var absence = Recorded(_ines, Today.AddDays(10), Today.AddDays(12));

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = absence.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(StaffLeaveStatus.Cancelled, absence.Status);
    }

    [Fact]
    public async Task A_Roster_That_Cannot_Be_Read_Stops_The_Decision_Before_Anything_Is_Saved()
    {
        SignIn(_admin);
        var leave = Stored(_ines, Mon16Nov, Wed18Nov);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Decide().Handle(new DecideStaffLeaveRequestCommand { Id = leave.Id, Approve = true }, default);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Secretary_Cannot_Withdraw_A_Colleagues_Request()
    {
        var colleague = Account(User.RoleSecretary, "Amira Ben Salah");
        SignIn(colleague);
        var leave = Stored(_ines, Today.AddDays(10), Today.AddDays(12));

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = leave.Id }, default);

        Assert.Equal(StaffLeaveRules.NotOwnRequest, result.Error);
        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);
    }

    [Fact]
    public async Task Withdrawing_A_Request_Of_Another_Clinic_Reads_As_Not_Found()
    {
        SignIn(_admin);
        var foreign = Stored(_ines, Today.AddDays(10), Today.AddDays(12), clinicId: OtherClinicId);

        var result = await Cancel().Handle(new CancelStaffLeaveRequestCommand { Id = foreign.Id }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
    }


    private RecordStaffAbsenceCommand Absence(User target, DateOnly? start = null, DateOnly? end = null) => new()
    {
        UserId = target.Id,
        Type = "Unpaid",
        StartDate = start ?? Mon16Nov,
        EndDate = end ?? Wed18Nov,
        Reason = "Absence non prévenue",
    };

    [Fact]
    public async Task An_Absence_Is_Saved_Accepted_And_She_Is_Told()
    {
        SignIn(_admin);

        var result = await RecordAbsence().Handle(Absence(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.EnteredByAdmin);
        Assert.Equal("Approved", result.Value.Status);
        _requests.Verify(r => r.AddAsync(It.Is<StaffLeaveRequest>(l =>
            l.Status == StaffLeaveStatus.Approved && l.EnteredByAdmin && l.UserId == _ines.Id && l.Type == StaffLeaveType.Unpaid),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.StaffLeaveAsync(
            ClinicId, NotificationCategory.StaffLeaveApproved, "Absence enregistrée",
            It.Is<string>(m => m.StartsWith("Une absence (sans solde)")), _admin.Id, _ines.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Absence_Is_Only_For_A_Secretary()
    {
        SignIn(_admin);
        var doctor = Account(User.RoleDoctor, "Dr Nadia Trabelsi");

        var result = await RecordAbsence().Handle(Absence(doctor), default);

        Assert.True(result.IsFailure);
        VerifyNoWrite();
    }

    [Fact]
    public async Task An_Account_Of_Another_Clinic_Reads_As_Not_Found()
    {
        SignIn(_admin);
        var foreign = Account(User.RoleSecretary, "Secrétaire Ailleurs", OtherClinicId);

        var result = await RecordAbsence().Handle(Absence(foreign), default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
        VerifyNoWrite();
    }

    [Fact]
    public async Task Only_An_Admin_Records_An_Absence()
    {
        SignIn(_ines);

        var result = await RecordAbsence().Handle(Absence(_ines), default);

        Assert.Equal(StaffLeaveRules.AdminOnly, result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task An_Absence_Over_Her_Congé_Is_Refused_Naming_Her()
    {
        SignIn(_admin);
        var existing = Stored(_ines, Mon16Nov, Wed18Nov, StaffLeaveStatus.Approved);
        _requests.Setup(r => r.FindHoldingOverlapAsync(ClinicId, _ines.Id, Mon16Nov, Wed18Nov, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await RecordAbsence().Handle(Absence(_ines), default);

        Assert.Equal("Ines Gharbi a déjà un congé accepté du 16/11 au 18/11.", result.Error);
        VerifyNoWrite();
    }

    [Fact]
    public async Task An_Absence_Over_Her_Waiting_Request_Says_It_Is_Waiting()
    {
        SignIn(_admin);
        var waiting = Stored(_ines, Mon16Nov, Wed18Nov);
        _requests.Setup(r => r.FindHoldingOverlapAsync(ClinicId, _ines.Id, Mon16Nov, Wed18Nov, It.IsAny<CancellationToken>()))
            .ReturnsAsync(waiting);

        var result = await RecordAbsence().Handle(Absence(_ines), default);

        Assert.Equal("Ines Gharbi a déjà une demande en attente du 16/11 au 18/11 : répondez-y plutôt.", result.Error);
        VerifyNoWrite();
    }


    [Fact]
    public async Task An_Admin_Sets_The_Yearly_Allowance_And_Reads_The_Balance_Back()
    {
        SignIn(_admin);
        _requests.Setup(r => r.GetByUserAsync(ClinicId, _ines.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<StaffLeaveRequest>());

        var result = await SetAllowance().Handle(new SetLeaveAllowanceCommand { UserId = _ines.Id, AllowanceDays = 24m }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(24m, _ines.AnnualLeaveAllowanceDays);
        Assert.Equal(24m, result.Value!.Remaining);
        _users.Verify(r => r.Update(_ines), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Empty_Allowance_Clears_It()
    {
        SignIn(_admin);
        _ines.SetAnnualLeaveAllowance(24m);
        _requests.Setup(r => r.GetByUserAsync(ClinicId, _ines.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<StaffLeaveRequest>());

        var result = await SetAllowance().Handle(new SetLeaveAllowanceCommand { UserId = _ines.Id, AllowanceDays = null }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(_ines.AnnualLeaveAllowanceDays);
        Assert.Null(result.Value!.Remaining);
    }

    [Fact]
    public async Task An_Allowance_Outside_Half_Days_Is_Refused()
    {
        SignIn(_admin);

        var result = await SetAllowance().Handle(new SetLeaveAllowanceCommand { UserId = _ines.Id, AllowanceDays = 24.25m }, default);

        Assert.True(result.IsFailure);
        Assert.Null(_ines.AnnualLeaveAllowanceDays);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Allowance_Is_Refused_While_The_Module_Is_Off()
    {
        SignIn(_admin);
        _clinic.SetLeaveRequestsEnabled(false);

        var result = await SetAllowance().Handle(new SetLeaveAllowanceCommand { UserId = _ines.Id, AllowanceDays = 24m }, default);

        Assert.Equal(StaffLeaveRules.ModuleOff, result.Error);
        Assert.Null(_ines.AnnualLeaveAllowanceDays);
    }

    [Fact]
    public async Task The_Allowance_Of_Another_Clinics_Account_Reads_As_Not_Found()
    {
        SignIn(_admin);
        var foreign = Account(User.RoleSecretary, "Secrétaire Ailleurs", OtherClinicId);

        var result = await SetAllowance().Handle(new SetLeaveAllowanceCommand { UserId = foreign.Id, AllowanceDays = 24m }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
        Assert.Null(foreign.AnnualLeaveAllowanceDays);
    }

    [Fact]
    public async Task Only_An_Admin_Sets_An_Allowance()
    {
        SignIn(_ines);

        var result = await SetAllowance().Handle(new SetLeaveAllowanceCommand { UserId = _ines.Id, AllowanceDays = 30m }, default);

        Assert.Equal(StaffLeaveRules.AdminOnly, result.Error);
        Assert.Null(_ines.AnnualLeaveAllowanceDays);
    }


    private SetTeamModulesCommandHandler Modules() => new(_clinics.Object, _users.Object, _context.Object, _uow.Object);

    [Fact]
    public async Task An_Admin_Turns_The_Module_On()
    {
        _clinic.SetLeaveRequestsEnabled(false);
        SignIn(_admin);

        var result = await Modules().Handle(new SetTeamModulesCommand { LeaveRequests = true, Version = 4 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(_clinic.LeaveRequestsEnabled);
        _uow.Verify(u => u.SetExpectedVersion(_clinic, 4u), Times.Once);
        _clinics.Verify(r => r.UpdateAsync(_clinic, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task An_Unchanged_Or_Omitted_Switch_Writes_Nothing(bool? leaveRequests)
    {
        SignIn(_admin);

        var result = await Modules().Handle(new SetTeamModulesCommand { LeaveRequests = leaveRequests }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.LeaveRequestsEnabled);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Only_An_Admin_Turns_The_Module_On_Or_Off()
    {
        SignIn(_ines);

        var result = await Modules().Handle(new SetTeamModulesCommand { LeaveRequests = false }, default);

        Assert.True(result.IsFailure);
        Assert.True(_clinic.LeaveRequestsEnabled);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Pointage_Is_Off_By_Default_And_Turns_On_Without_Touching_The_Congés()
    {
        Assert.False(new Clinic(OtherClinicId, "Neuf", code: "CODE02").AttendanceEnabled);
        SignIn(_admin);

        var result = await Modules().Handle(new SetTeamModulesCommand { Attendance = true, Version = 6 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(_clinic.AttendanceEnabled);
        Assert.True(result.Value!.AttendanceEnabled);
        Assert.True(_clinic.LeaveRequestsEnabled);
        _uow.Verify(u => u.SetExpectedVersion(_clinic, 6u), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Turning_The_Congés_Off_Leaves_The_Pointage_Switch_As_It_Was()
    {
        _clinic.SetAttendanceEnabled(true);
        SignIn(_admin);

        var result = await Modules().Handle(new SetTeamModulesCommand { LeaveRequests = false }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_clinic.LeaveRequestsEnabled);
        Assert.True(_clinic.AttendanceEnabled);
    }
}

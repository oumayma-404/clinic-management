using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// The congé reads: the admin's lists (with the balance after and who else is off), the filters reaching the
/// repository, the team strip, a secretary's own page and file, the form's preview, and the single re-read a decision
/// makes — each scoped to the caller's cabinet, and to the caller herself unless she is an admin.
/// </summary>
public class StaffLeaveQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly Mon16Nov = new(2026, 11, 16);
    private static readonly DateOnly Wed18Nov = new(2026, 11, 18);
    private static readonly DateOnly Wed30Dec = new(2026, 12, 30);
    private static readonly DateOnly Thu31Dec = new(2026, 12, 31);
    private static readonly DateOnly Sat2Jan = new(2027, 1, 2);

    private readonly Mock<IStaffLeaveRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly List<User> _roster = new();
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");

    private readonly User _ines;
    private readonly User _amira;
    private readonly User _admin;

    public StaffLeaveQueryTests()
    {
        _clinic.SetLeaveRequestsEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_roster.ToList()));
        _requests.Setup(r => r.GetApprovedOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffLeaveRequest>());
        _requests.Setup(r => r.GetByUserAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffLeaveRequest>());

        _ines = Account(User.RoleSecretary, "Ines Gharbi");
        _amira = Account(User.RoleSecretary, "Amira Ben Salah");
        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
        _ines.SetAnnualLeaveAllowance(24m);
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

    private StaffLeaveRequest Leave(
        User owner, DateOnly start, DateOnly end, decimal days, StaffLeaveStatus status = StaffLeaveStatus.Pending,
        StaffLeaveType type = StaffLeaveType.PaidLeave, decimal? counted = null, Guid? clinicId = null)
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), clinicId ?? ClinicId, owner.Id, type, start, end, false, "Motif", days);
        if (status == StaffLeaveStatus.Approved) leave.Approve(_admin.Id, counted ?? days);
        if (status == StaffLeaveStatus.Rejected) leave.Reject(_admin.Id, "Non");
        _requests.Setup(r => r.GetByIdAsync(leave.Id, It.IsAny<CancellationToken>())).ReturnsAsync(leave);
        return leave;
    }

    private void History(User owner, params StaffLeaveRequest[] requests) =>
        _requests.Setup(r => r.GetByUserAsync(ClinicId, owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(requests);

    private static DateOnly Today => StaffLeaveRules.ClinicToday();

    private ListStaffLeaveRequestsQueryHandler List() => new(_requests.Object, _users.Object, _clinics.Object, _context.Object);

    [Fact]
    public async Task A_Waiting_Request_Shows_The_Balance_After_And_Who_Else_Is_Off()
    {
        SignIn(_admin);
        var accepted = Leave(_ines, Mon16Nov, Wed18Nov, 3m, StaffLeaveStatus.Approved, counted: 2.5m);
        var waiting = Leave(_ines, Wed30Dec, Sat2Jan, 4m);
        History(_ines, accepted, waiting);
        var amirasCongé = Leave(_amira, Wed30Dec, Thu31Dec, 2m, StaffLeaveStatus.Approved);
        _requests.Setup(r => r.ListAsync(ClinicId, true, null, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<StaffLeaveRequest>.Unpaged(new[] { waiting }));
        _requests.Setup(r => r.GetApprovedOverlappingAsync(ClinicId, Wed30Dec, Sat2Jan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { amirasCongé, Leave(_ines, Thu31Dec, Thu31Dec, 1m, StaffLeaveStatus.Approved) });

        var result = await List().Handle(new ListStaffLeaveRequestsQuery { Status = "pending" }, default);

        Assert.True(result.IsSuccess, result.Error);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Ines Gharbi", item.UserName);
        Assert.Equal(19.5m, item.BalanceAfter);
        Assert.Equal(new[] { "Amira Ben Salah" }, item.OthersOff);
    }

    [Fact]
    public async Task A_Decided_Request_Carries_No_Balance_After_Nor_Others_Off()
    {
        SignIn(_admin);
        var decided = Leave(_ines, Mon16Nov, Wed18Nov, 3m, StaffLeaveStatus.Approved);
        _requests.Setup(r => r.ListAsync(ClinicId, false, null, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<StaffLeaveRequest>.Unpaged(new[] { decided }));

        var result = await List().Handle(new ListStaffLeaveRequestsQuery { Status = "decided" }, default);

        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.BalanceAfter);
        Assert.Empty(item.OthersOff);
        Assert.Equal("Dr Salma Ben Youssef", item.DecidedByName);
        _requests.Verify(r => r.GetApprovedOverlappingAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<string?, bool?, StaffLeaveStatus?> Filters => new()
    {
        { "pending", true, null },
        { "decided", false, null },
        { "approved", false, StaffLeaveStatus.Approved },
        { "Rejected", false, StaffLeaveStatus.Rejected },
        { " cancelled ", false, StaffLeaveStatus.Cancelled },
        { null, null, null },
        { "", null, null },
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Each_Status_Filter_Reaches_The_Repository(string? status, bool? pendingOnly, StaffLeaveStatus? only)
    {
        SignIn(_admin);
        _requests.Setup(r => r.ListAsync(ClinicId, It.IsAny<bool?>(), It.IsAny<StaffLeaveStatus?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<StaffLeaveRequest>.Unpaged(Array.Empty<StaffLeaveRequest>()));

        var result = await List().Handle(new ListStaffLeaveRequestsQuery { Status = status, Page = 2, PageSize = 10 }, default);

        Assert.True(result.IsSuccess, result.Error);
        _requests.Verify(r => r.ListAsync(ClinicId, pendingOnly, only, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Unknown_Status_Filter_Is_Refused_Rather_Than_Read_As_Everything()
    {
        SignIn(_admin);

        var result = await List().Handle(new ListStaffLeaveRequestsQuery { Status = "approuve" }, default);

        Assert.Equal(StaffLeaveRules.UnknownStatusFilter, result.Error);
        _requests.Verify(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<StaffLeaveStatus?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Every_Congé_Read_Is_Refused_While_The_Module_Is_Off()
    {
        _clinic.SetLeaveRequestsEnabled(false);

        SignIn(_admin);
        var list = await List().Handle(new ListStaffLeaveRequestsQuery { Status = "pending" }, default);
        var team = await Overview().Handle(new GetTeamLeaveOverviewQuery(), default);
        var file = await new GetStaffFileQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetStaffFileQuery { UserId = _ines.Id }, default);
        SignIn(_ines);
        var own = await new GetMyStaffLeaveQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetMyStaffLeaveQuery(), default);
        var preview = await Preview().Handle(new PreviewStaffLeaveQuery { Type = "PaidLeave", StartDate = Mon16Nov, EndDate = Wed18Nov }, default);

        Assert.All(new[] { list.Error, team.Error, file.Error, own.Error, preview.Error }, e => Assert.Equal(StaffLeaveRules.ModuleOff, e));
    }

    [Fact]
    public async Task Only_An_Admin_Lists_The_Cabinets_Requests()
    {
        SignIn(_ines);

        var result = await List().Handle(new ListStaffLeaveRequestsQuery { Status = "pending" }, default);

        Assert.Equal(StaffLeaveRules.AdminOnly, result.Error);
        _requests.Verify(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<StaffLeaveStatus?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Pending_Count_Is_Zero_While_The_Module_Is_Off()
    {
        SignIn(_admin);
        _clinic.SetLeaveRequestsEnabled(false);
        _requests.Setup(r => r.CountPendingAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await new GetPendingStaffLeaveCountQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetPendingStaffLeaveCountQuery(), default);

        Assert.Equal(0, result.Value!.Count);
        _requests.Verify(r => r.CountPendingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Pending_Count_Is_The_Repositorys_While_On()
    {
        SignIn(_admin);
        _requests.Setup(r => r.CountPendingAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await new GetPendingStaffLeaveCountQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetPendingStaffLeaveCountQuery(), default);

        Assert.Equal(3, result.Value!.Count);
    }

    [Fact]
    public async Task Her_Own_Page_Holds_Her_Balance_And_Her_Requests()
    {
        SignIn(_ines);
        History(_ines,
            Leave(_ines, Mon16Nov, Wed18Nov, 3m, StaffLeaveStatus.Approved, counted: 2.5m),
            Leave(_ines, Wed30Dec, Sat2Jan, 4m));

        var result = await new GetMyStaffLeaveQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetMyStaffLeaveQuery { Year = 2026 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(24m, result.Value!.Balance.Allowance);
        Assert.Equal(2.5m, result.Value.Balance.Taken);
        Assert.Equal(21.5m, result.Value.Balance.Remaining);
        Assert.Equal(2m, result.Value.Balance.Pending);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task Her_File_Lists_The_Accepted_Absences_By_Type()
    {
        SignIn(_admin);
        History(_ines,
            Leave(_ines, Mon16Nov, Wed18Nov, 3m, StaffLeaveStatus.Approved, counted: 2.5m),
            Leave(_ines, Mon16Nov.AddDays(-12), Mon16Nov.AddDays(-10), 3m, StaffLeaveStatus.Approved, StaffLeaveType.Unpaid),
            Leave(_ines, Mon16Nov.AddDays(9), Mon16Nov.AddDays(9), 1m, StaffLeaveStatus.Rejected, StaffLeaveType.Sick));

        var result = await new GetStaffFileQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetStaffFileQuery { UserId = _ines.Id, Year = 2026 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Ines Gharbi", result.Value!.FullName);
        Assert.Equal(2.5m, result.Value.AcceptedByType.Single(t => t.Type == "PaidLeave").Days);
        Assert.Equal(3m, result.Value.AcceptedByType.Single(t => t.Type == "Unpaid").Days);
        Assert.Equal(0m, result.Value.AcceptedByType.Single(t => t.Type == "Sick").Days);
        Assert.Equal(3, result.Value.Items.Count);
    }

    [Fact]
    public async Task The_File_Of_Another_Clinics_Account_Reads_As_Not_Found()
    {
        SignIn(_admin);
        var foreign = Account(User.RoleSecretary, "Secrétaire Ailleurs", OtherClinicId);

        var result = await new GetStaffFileQueryHandler(_requests.Object, _users.Object, _clinics.Object, _context.Object)
            .Handle(new GetStaffFileQuery { UserId = foreign.Id }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
    }

    private PreviewStaffLeaveQueryHandler Preview() => new(_requests.Object, _users.Object, _clinics.Object, _context.Object);

    [Fact]
    public async Task The_Preview_Counts_The_Days_And_Only_This_Years_Share()
    {
        SignIn(_ines);
        History(_ines, Leave(_ines, Mon16Nov, Wed18Nov, 3m, StaffLeaveStatus.Approved, counted: 2.5m));

        var result = await Preview().Handle(new PreviewStaffLeaveQuery
        {
            Type = "PaidLeave", StartDate = Wed30Dec, EndDate = Sat2Jan,
        }, default);

        Assert.Equal(4m, result.Value!.Days);
        Assert.Equal(19.5m, result.Value.BalanceAfter);
    }

    [Fact]
    public async Task A_Preview_Other_Than_Congé_Payé_Announces_No_Balance()
    {
        SignIn(_ines);

        var result = await Preview().Handle(new PreviewStaffLeaveQuery { Type = "Sick", StartDate = Mon16Nov, EndDate = Mon16Nov, HalfDay = true }, default);

        Assert.Equal(0.5m, result.Value!.Days);
        Assert.Null(result.Value.BalanceAfter);
    }

    [Fact]
    public async Task Only_An_Admin_Previews_For_Someone_Else()
    {
        SignIn(_amira);

        var result = await Preview().Handle(new PreviewStaffLeaveQuery
        {
            UserId = _ines.Id, Type = "PaidLeave", StartDate = Mon16Nov, EndDate = Wed18Nov,
        }, default);

        Assert.Equal(StaffLeaveRules.AdminOnly, result.Error);
    }

    [Fact]
    public async Task An_Admin_Previews_With_The_Secretarys_Own_Allowance()
    {
        SignIn(_admin);

        var result = await Preview().Handle(new PreviewStaffLeaveQuery
        {
            UserId = _ines.Id, Type = "PaidLeave", StartDate = Mon16Nov, EndDate = Wed18Nov,
        }, default);

        Assert.Equal(21m, result.Value!.BalanceAfter);
    }

    private GetStaffLeaveRequestQueryHandler Single() => new(_requests.Object, _users.Object, _context.Object);

    [Fact]
    public async Task A_Colleagues_Request_Reads_As_Not_Found_To_A_Secretary()
    {
        SignIn(_amira);
        var leave = Leave(_ines, Mon16Nov, Wed18Nov, 3m);

        var result = await Single().Handle(new GetStaffLeaveRequestQuery { Id = leave.Id }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
    }

    [Fact]
    public async Task The_Owner_And_An_Admin_Read_It_With_Its_Version()
    {
        var leave = Leave(_ines, Mon16Nov, Wed18Nov, 3m);

        SignIn(_ines);
        var own = await Single().Handle(new GetStaffLeaveRequestQuery { Id = leave.Id }, default);
        SignIn(_admin);
        var admins = await Single().Handle(new GetStaffLeaveRequestQuery { Id = leave.Id }, default);

        Assert.True(own.IsSuccess, own.Error);
        Assert.True(admins.IsSuccess, admins.Error);
        Assert.Equal("Pending", admins.Value!.Status);
    }

    [Fact]
    public async Task A_Request_Of_Another_Clinic_Reads_As_Not_Found_Even_To_An_Admin()
    {
        SignIn(_admin);
        var foreign = Leave(_ines, Mon16Nov, Wed18Nov, 3m, clinicId: OtherClinicId);

        var result = await Single().Handle(new GetStaffLeaveRequestQuery { Id = foreign.Id }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
    }

    private GetTeamLeaveOverviewQueryHandler Overview() => new(_requests.Object, _users.Object, _clinics.Object, _context.Object);

    [Fact]
    public async Task The_Team_Strip_Lists_Active_Secretaries_With_Their_Next_Accepted_Congé()
    {
        SignIn(_admin);
        Account(User.RoleDoctor, "Dr Nadia Trabelsi");
        var gone = Account(User.RoleSecretary, "Ancienne Secrétaire");
        gone.Deactivate();
        var past = Leave(_ines, Today.AddDays(-20), Today.AddDays(-18), 3m, StaffLeaveStatus.Approved);
        var waiting = Leave(_ines, Today.AddDays(3), Today.AddDays(3), 1m);
        var next = Leave(_ines, Today.AddDays(10), Today.AddDays(12), 3m, StaffLeaveStatus.Approved);
        var later = Leave(_ines, Today.AddDays(40), Today.AddDays(41), 2m, StaffLeaveStatus.Approved);
        _requests.Setup(r => r.ListAsync(ClinicId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<StaffLeaveRequest>.Unpaged(new[] { later, past, waiting, next }));

        var result = await Overview().Handle(new GetTeamLeaveOverviewQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "Amira Ben Salah", "Ines Gharbi" }, result.Value!.Members.Select(m => m.FullName));
        var ines = result.Value.Members.Single(m => m.UserId == _ines.Id);
        Assert.Equal(next.StartDate, ines.NextStartDate);
        Assert.Equal(next.EndDate, ines.NextEndDate);
        Assert.Equal(24m, ines.Balance.Allowance);
        var amira = result.Value.Members.Single(m => m.UserId == _amira.Id);
        Assert.Null(amira.NextStartDate);
        Assert.Null(amira.Balance.Remaining);
    }

    [Fact]
    public async Task Only_An_Admin_Reads_The_Team_Strip()
    {
        SignIn(_ines);

        var result = await Overview().Handle(new GetTeamLeaveOverviewQuery(), default);

        Assert.Equal(StaffLeaveRules.AdminOnly, result.Error);
    }
}

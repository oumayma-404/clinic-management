using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// « Droit à congé » writes: a rule, a starting balance, days added or removed, a line cancelled. Each appends (or strikes
/// through) and answers the folded balance; only an admin of the cabinet, with congés on, for an account of hers.
/// </summary>
public class StaffLeaveAllowanceCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IStaffLeaveAllowanceRepository> _allowances = new();
    private readonly Mock<IStaffLeaveRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");
    private readonly List<StaffLeaveAllowanceEntry> _stored = new();
    private readonly User _ines;
    private readonly User _admin;

    private static DateOnly Today => StaffLeaveRules.ClinicToday();
    private static DateOnly ThisJanuary => new(Today.Year, 1, 1);

    public StaffLeaveAllowanceCommandTests()
    {
        _clinic.SetLeaveRequestsEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _requests.Setup(r => r.GetByUserAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StaffLeaveRequest>());
        _allowances.Setup(r => r.AddAsync(It.IsAny<StaffLeaveAllowanceEntry>(), It.IsAny<CancellationToken>()))
            .Callback<StaffLeaveAllowanceEntry, CancellationToken>((e, _) => _stored.Add(e));
        _allowances.Setup(r => r.GetForUserAsync(ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string userId, CancellationToken _) => _stored.Where(e => e.UserId == userId).ToList());

        _ines = Account(User.RoleSecretary, "Ines Gharbi");
        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
        SignIn(_admin);
    }

    private User Account(string role, string name, Guid? clinicId = null)
    {
        var user = User.CreateLocalUser(clinicId ?? ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private void SignIn(User user) => _context.Setup(c => c.GetUserId()).Returns(user.Id);

    private SetLeaveRuleCommandHandler Rule() =>
        new(_allowances.Object, _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private SetOpeningLeaveBalanceCommandHandler Opening() =>
        new(_allowances.Object, _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private AdjustLeaveBalanceCommandHandler Adjust() =>
        new(_allowances.Object, _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private CancelLeaveAllowanceEntryCommandHandler Cancel() =>
        new(_allowances.Object, _requests.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private void VerifyNothingWritten()
    {
        Assert.Empty(_stored);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Admin_Sets_A_Yearly_Rule_And_Reads_The_Balance_Back()
    {
        var result = await Rule().Handle(new SetLeaveRuleCommand
        {
            UserId = _ines.Id, Days = 20, Accrual = "Year", CarryOver = false, Since = ThisJanuary,
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        var entry = Assert.Single(_stored);
        Assert.Equal(StaffLeaveAllowanceKind.Rule, entry.Kind);
        Assert.Equal(StaffLeaveAccrual.Year, entry.Accrual);
        Assert.Equal("Dr Salma Ben Youssef", entry.CreatedByName);
        Assert.Equal(20m, result.Value!.Remaining);
        Assert.Equal("Year", result.Value.Rule!.Accrual);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Monthly_Rule_From_Next_Month_Announces_Its_First_Credit()
    {
        var next = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1);

        var result = await Rule().Handle(new SetLeaveRuleCommand { UserId = _ines.Id, Days = 2, Accrual = "month", Since = next }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(next, result.Value!.NextCredit!.Day);
        Assert.Equal(2m, result.Value.NextCredit.Days);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Week")]
    public async Task A_Rule_Needs_Par_An_Or_Par_Mois(string? accrual)
    {
        var result = await Rule().Handle(new SetLeaveRuleCommand { UserId = _ines.Id, Days = 2, Accrual = accrual, Since = ThisJanuary }, default);

        Assert.Equal(StaffLeaveAllowanceWrites.AccrualRequired, result.Error);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task A_Rule_Off_A_1st_Or_In_A_Mistyped_Year_Is_Refused()
    {
        var odd = await Rule().Handle(new SetLeaveRuleCommand { UserId = _ines.Id, Days = 2, Accrual = "Month", Since = ThisJanuary.AddDays(5) }, default);
        var far = await Rule().Handle(new SetLeaveRuleCommand { UserId = _ines.Id, Days = 20, Accrual = "Year", Since = ThisJanuary.AddYears(30) }, default);

        Assert.Equal("Un droit par mois commence le 1er d'un mois.", odd.Error);
        Assert.Equal(StaffLeaveAllowanceWrites.TooFar, far.Error);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task A_Starting_Balance_Is_What_She_Has_Left_Today()
    {
        var result = await Opening().Handle(new SetOpeningLeaveBalanceCommand { UserId = _ines.Id, Days = 10, OnDay = Today }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(StaffLeaveAllowanceKind.Opening, Assert.Single(_stored).Kind);
        Assert.Equal(10m, result.Value!.Opening);
        Assert.Equal(10m, result.Value.Remaining);
    }

    [Fact]
    public async Task A_Starting_Balance_In_The_Future_Is_Refused()
    {
        var result = await Opening().Handle(new SetOpeningLeaveBalanceCommand { UserId = _ines.Id, Days = 10, OnDay = Today.AddDays(3) }, default);

        Assert.Equal(StaffLeaveAllowanceWrites.FutureDay, result.Error);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Days_Added_Need_A_Motif_And_Move_The_Balance()
    {
        var refused = await Adjust().Handle(new AdjustLeaveBalanceCommand { UserId = _ines.Id, Days = 2, OnDay = Today, Reason = " " }, default);
        Assert.Equal("Indiquez le motif.", refused.Error);
        VerifyNothingWritten();

        await Opening().Handle(new SetOpeningLeaveBalanceCommand { UserId = _ines.Id, Days = 10, OnDay = Today }, default);
        var result = await Adjust().Handle(new AdjustLeaveBalanceCommand { UserId = _ines.Id, Days = -1.5m, OnDay = Today, Reason = "Rattrapage" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(8.5m, result.Value!.Remaining);
        Assert.Equal("Rattrapage", _stored[^1].Reason);
    }

    [Fact]
    public async Task A_Line_Is_Cancelled_With_A_Motif_Once_And_The_Balance_Forgets_It()
    {
        await Opening().Handle(new SetOpeningLeaveBalanceCommand { UserId = _ines.Id, Days = 10, OnDay = Today }, default);
        await Adjust().Handle(new AdjustLeaveBalanceCommand { UserId = _ines.Id, Days = 5, OnDay = Today, Reason = "Prime" }, default);
        var added = _stored[^1];
        _allowances.Setup(r => r.GetByIdAsync(added.Id, It.IsAny<CancellationToken>())).ReturnsAsync(added);

        var noMotif = await Cancel().Handle(new CancelLeaveAllowanceEntryCommand { EntryId = added.Id }, default);
        var result = await Cancel().Handle(new CancelLeaveAllowanceEntryCommand { EntryId = added.Id, Reason = "Erreur" }, default);
        var again = await Cancel().Handle(new CancelLeaveAllowanceEntryCommand { EntryId = added.Id, Reason = "Encore" }, default);

        Assert.Equal(StaffLeaveAllowanceWrites.CancelReasonRequired, noMotif.Error);
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(added.IsCancelled);
        Assert.Equal(10m, result.Value!.Remaining);
        Assert.Equal(StaffLeaveAllowanceWrites.EntryCancelledCode, again.Code);
    }

    [Fact]
    public async Task Another_Cabinets_Account_Or_Line_Reads_As_Not_Found()
    {
        var foreign = Account(User.RoleSecretary, "Secrétaire Ailleurs", OtherClinicId);
        var foreignLine = StaffLeaveAllowanceEntry.Opening(Guid.NewGuid(), OtherClinicId, foreign.Id, 5, Today, "x", "x", DateTime.UtcNow);
        _allowances.Setup(r => r.GetByIdAsync(foreignLine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignLine);

        var rule = await Rule().Handle(new SetLeaveRuleCommand { UserId = foreign.Id, Days = 20, Accrual = "Year", Since = ThisJanuary }, default);
        var cancel = await Cancel().Handle(new CancelLeaveAllowanceEntryCommand { EntryId = foreignLine.Id, Reason = "x" }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, rule.Code);
        Assert.Equal(StaffLeaveRules.NotFoundCode, cancel.Code);
        Assert.False(foreignLine.IsCancelled);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Only_An_Admin_With_Congés_On_Writes_A_Droit()
    {
        SignIn(_ines);
        var secretary = await Opening().Handle(new SetOpeningLeaveBalanceCommand { UserId = _ines.Id, Days = 30, OnDay = Today }, default);
        SignIn(_admin);
        _clinic.SetLeaveRequestsEnabled(false);
        var off = await Opening().Handle(new SetOpeningLeaveBalanceCommand { UserId = _ines.Id, Days = 30, OnDay = Today }, default);

        Assert.Equal(StaffLeaveRules.AdminOnly, secretary.Error);
        Assert.Equal(StaffLeaveRules.ModuleOff, off.Error);
        VerifyNothingWritten();
    }
}

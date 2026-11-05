using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// The arithmetic every congé screen reads: open days counted from the cabinet's hours, the year a day belongs to,
/// what is taken and waiting, and the « solde après » a form announces before anything is sent.
/// </summary>
public class StaffLeaveRulesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Secretary = "local|secretary";
    private const string Admin = "local|admin";

    private static readonly DateOnly Mon16Nov = new(2026, 11, 16);
    private static readonly DateOnly Sun22Nov = new(2026, 11, 22);
    private static readonly DateOnly Wed30Dec = new(2026, 12, 30);
    private static readonly DateOnly Sat2Jan = new(2027, 1, 2);

    private static string Hours(params DayOfWeek[] open) =>
        "[" + string.Join(",", Enum.GetValues<DayOfWeek>().Select(d =>
            $"{{\"Day\":\"{d}\",\"Enabled\":{(open.Contains(d) ? "true" : "false")},\"From\":\"09:00\",\"To\":\"17:00\",\"BreakFrom\":null,\"BreakTo\":null}}")) + "]";

    private static StaffLeaveRequest Leave(
        StaffLeaveType type, DateOnly start, DateOnly end, decimal days, StaffLeaveStatus status, decimal? counted = null)
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), ClinicId, Secretary, type, start, end, false, "Motif", days);
        if (status == StaffLeaveStatus.Approved) leave.Approve(Admin, counted ?? days);
        if (status == StaffLeaveStatus.Rejected) leave.Reject(Admin, "Non");
        if (status == StaffLeaveStatus.Cancelled) leave.Cancel(Secretary, start.AddDays(-30));
        return leave;
    }

    [Fact]
    public void Closed_Days_Of_The_Cabinet_Are_Not_Counted()
    {
        var weekdaysOnly = Hours(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);

        Assert.Equal(5m, StaffLeaveRules.CountDays(Mon16Nov, Sun22Nov, false, weekdaysOnly));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Unset_Or_Unreadable_Hours_Count_Monday_To_Saturday(string? hours)
    {
        Assert.Equal(6m, StaffLeaveRules.CountDays(Mon16Nov, Sun22Nov, false, hours));
    }

    [Fact]
    public void A_Half_Day_Counts_Half_And_An_Inverted_Range_Counts_Nothing()
    {
        Assert.Equal(0.5m, StaffLeaveRules.CountDays(Mon16Nov, Mon16Nov, true, null));
        Assert.Equal(0m, StaffLeaveRules.CountDays(Sun22Nov, Mon16Nov, false, null));
    }

    [Fact]
    public void A_Congé_Across_The_New_Year_Is_Split_By_Its_Open_Days()
    {
        var leave = Leave(StaffLeaveType.PaidLeave, Wed30Dec, Sat2Jan, 4m, StaffLeaveStatus.Approved);

        Assert.Equal(2m, StaffLeaveRules.DaysInYear(leave, 2026, null));
        Assert.Equal(2m, StaffLeaveRules.DaysInYear(leave, 2027, null));
        Assert.Equal(0m, StaffLeaveRules.DaysInYear(leave, 2028, null));
    }

    [Fact]
    public void The_Jour_De_L_An_Taken_Out_In_Décompter_Comes_Off_The_New_Year()
    {
        var leave = Leave(StaffLeaveType.PaidLeave, Wed30Dec, Sat2Jan, 4m, StaffLeaveStatus.Approved, counted: 3m);

        Assert.Equal(2m, StaffLeaveRules.DaysInYear(leave, 2026, null));
        Assert.Equal(1m, StaffLeaveRules.DaysInYear(leave, 2027, null));
    }

    [Fact]
    public void Only_Accepted_Congé_Payé_Is_Taken()
    {
        var requests = new[]
        {
            Leave(StaffLeaveType.PaidLeave, Mon16Nov, Mon16Nov.AddDays(2), 3m, StaffLeaveStatus.Approved, counted: 2.5m),
            Leave(StaffLeaveType.Sick, Mon16Nov.AddDays(10), Mon16Nov.AddDays(10), 1m, StaffLeaveStatus.Approved),
            Leave(StaffLeaveType.PaidLeave, Mon16Nov.AddDays(20), Mon16Nov.AddDays(20), 1m, StaffLeaveStatus.Pending),
            Leave(StaffLeaveType.PaidLeave, Mon16Nov.AddDays(25), Mon16Nov.AddDays(25), 1m, StaffLeaveStatus.Rejected),
            Leave(StaffLeaveType.PaidLeave, Mon16Nov.AddDays(28), Mon16Nov.AddDays(28), 1m, StaffLeaveStatus.Cancelled),
        };

        Assert.Equal(2.5m, StaffLeaveRules.TakenIn(2026, requests, null));
    }

    [Fact]
    public void The_Balance_States_Taken_Remaining_And_What_Is_Waiting_For_The_Year()
    {
        var requests = new[]
        {
            Leave(StaffLeaveType.PaidLeave, Mon16Nov, Mon16Nov.AddDays(2), 3m, StaffLeaveStatus.Approved, counted: 2.5m),
            Leave(StaffLeaveType.PaidLeave, Wed30Dec, Sat2Jan, 4m, StaffLeaveStatus.Pending),
            Leave(StaffLeaveType.Sick, Mon16Nov.AddDays(5), Mon16Nov.AddDays(5), 1m, StaffLeaveStatus.Pending),
        };

        var balance = StaffLeaveRules.Balance(2026, 24m, requests, null);

        Assert.Equal(2026, balance.Year);
        Assert.Equal(2.5m, balance.Taken);
        Assert.Equal(21.5m, balance.Remaining);
        Assert.Equal(2m, balance.Pending);
    }

    [Fact]
    public void With_No_Allowance_There_Is_No_Remaining()
    {
        var balance = StaffLeaveRules.Balance(2026, null, Array.Empty<StaffLeaveRequest>(), null);

        Assert.Null(balance.Allowance);
        Assert.Null(balance.Remaining);
        Assert.Equal(0m, balance.Taken);
    }

    [Fact]
    public void The_Balance_After_A_Congé_Across_The_New_Year_Counts_Only_The_Start_Years_Share()
    {
        var history = new[]
        {
            Leave(StaffLeaveType.PaidLeave, Mon16Nov, Mon16Nov.AddDays(2), 3m, StaffLeaveStatus.Approved, counted: 2.5m),
        };

        var after = StaffLeaveRules.BalanceAfter(24m, history, Wed30Dec, Sat2Jan, 4m, null);

        Assert.Equal(19.5m, after);
    }

    [Fact]
    public void The_Balance_After_Equals_The_Balance_Once_Accepted()
    {
        var accepted = Leave(StaffLeaveType.PaidLeave, Mon16Nov, Mon16Nov.AddDays(2), 3m, StaffLeaveStatus.Approved, counted: 2.5m);
        var crossing = Leave(StaffLeaveType.PaidLeave, Wed30Dec, Sat2Jan, 4m, StaffLeaveStatus.Approved);

        var announced = StaffLeaveRules.BalanceAfter(24m, new[] { accepted }, Wed30Dec, Sat2Jan, 4m, null);
        var actual = StaffLeaveRules.Balance(2026, 24m, new[] { accepted, crossing }, null).Remaining;

        Assert.Equal(actual, announced);
    }

    [Fact]
    public void A_Same_Year_Congé_Deducts_All_Its_Days()
    {
        Assert.Equal(21m, StaffLeaveRules.BalanceAfter(24m, Array.Empty<StaffLeaveRequest>(), Mon16Nov, Mon16Nov.AddDays(2), 3m, null));
    }

    [Fact]
    public void Accepted_Absences_Are_Listed_For_Every_Type_Refused_And_Withdrawn_Ones_Not_Counted()
    {
        var requests = new[]
        {
            Leave(StaffLeaveType.PaidLeave, Mon16Nov, Mon16Nov.AddDays(2), 3m, StaffLeaveStatus.Approved, counted: 2.5m),
            Leave(StaffLeaveType.Unpaid, Mon16Nov.AddDays(-12), Mon16Nov.AddDays(-10), 3m, StaffLeaveStatus.Approved),
            Leave(StaffLeaveType.Sick, Mon16Nov.AddDays(9), Mon16Nov.AddDays(9), 0.5m, StaffLeaveStatus.Rejected),
            Leave(StaffLeaveType.Other, Mon16Nov.AddDays(28), Mon16Nov.AddDays(28), 1m, StaffLeaveStatus.Cancelled),
        };

        var totals = StaffLeaveRules.AcceptedByType(2026, requests, null);

        Assert.Equal(Enum.GetValues<StaffLeaveType>().Length, totals.Count);
        Assert.Equal(2.5m, totals.Single(t => t.Type == nameof(StaffLeaveType.PaidLeave)).Days);
        Assert.Equal(3m, totals.Single(t => t.Type == nameof(StaffLeaveType.Unpaid)).Days);
        Assert.Equal(0m, totals.Single(t => t.Type == nameof(StaffLeaveType.Sick)).Days);
        Assert.Equal(0m, totals.Single(t => t.Type == nameof(StaffLeaveType.Other)).Days);
        Assert.Equal("Sans solde", totals.Single(t => t.Type == nameof(StaffLeaveType.Unpaid)).TypeLabel);
    }

    [Fact]
    public void Every_Type_And_Status_Has_A_French_Label()
    {
        foreach (var type in Enum.GetValues<StaffLeaveType>())
            Assert.False(string.IsNullOrWhiteSpace(StaffLeaveRules.TypeLabel(type)));
        foreach (var status in Enum.GetValues<StaffLeaveStatus>())
            Assert.False(string.IsNullOrWhiteSpace(StaffLeaveRules.StatusLabel(status)));
    }

    [Theory]
    [InlineData("PaidLeave", StaffLeaveType.PaidLeave)]
    [InlineData("sick", StaffLeaveType.Sick)]
    [InlineData("Unpaid", StaffLeaveType.Unpaid)]
    [InlineData("42", null)]
    [InlineData("Vacances", null)]
    [InlineData(null, null)]
    public void Only_A_Known_Type_Name_Is_Parsed(string? value, StaffLeaveType? expected)
    {
        Assert.Equal(expected, StaffLeaveRules.ParseType(value));
    }
}

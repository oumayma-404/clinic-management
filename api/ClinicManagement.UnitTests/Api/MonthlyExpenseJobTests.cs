using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Expenses;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The daily pass that posts a monthly dépense's missing months (`caisse-monthly-expenses`).
///
/// <para><b>Every instant is a fixed literal</b>, which is why the pass takes « now » as a parameter — see
/// <c>AppointmentProgressJobTests</c> for the same reasoning, and <c>MonthlyExpenseScheduleTests</c> for the
/// calendar arithmetic itself. What this class holds is what neither can see: that the rows and the markers that
/// record them commit <b>together</b>, that a stopped series posts nothing, that one clinic's failure costs only
/// that clinic, and that the two declarations a job cannot work without are actually made.</para>
/// </summary>
public class MonthlyExpenseJobTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 5, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static RecurringExpense Series(
        Guid clinicId, string lastPosted, int dayOfMonth = 5, decimal amount = 800m) =>
        new(Guid.NewGuid(), clinicId, "Loyer", amount, PaymentMethod.Cash, dayOfMonth, lastPosted, "Local");

    private sealed class Harness
    {
        public Mock<IRecurringExpenseRepository> Series { get; } = new();
        public Mock<IExpenseRepository> Expenses { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();
        public Mock<IAuditActorProvider> AuditActor { get; } = new();

        // The real scope, not a mock: `UseSystemWide` is the declaration the query filters refuse without, and a
        // mock would accept a job that never made it.
        public TenantScope TenantScope { get; } = new(NullLogger<TenantScope>.Instance);

        public List<Expense> Posted { get; } = new();

        public Harness(params RecurringExpense[] active)
        {
            Series.Setup(r => r.GetActiveForPostingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
            Expenses
                .Setup(r => r.AddAsync(It.IsAny<Expense>(), It.IsAny<CancellationToken>()))
                .Callback<Expense, CancellationToken>((e, _) => Posted.Add(e))
                .ReturnsAsync((Expense e, CancellationToken _) => e);
        }

        public MonthlyExpenseJob Job() => new(
            Series.Object,
            Expenses.Object,
            UnitOfWork.Object,
            Realtime.Object,
            AuditActor.Object,
            TenantScope,
            NullLogger<MonthlyExpenseJob>.Instance);
    }

    // The headline: the month that has turned is posted, dated in the cabinet's own calendar.
    [Fact]
    public async Task A_Series_Owed_A_Month_Posts_It() // [AC-2]
    {
        var series = Series(ClinicA, lastPosted: "2026-08");
        var harness = new Harness(series);

        await harness.Job().PostDueMonthlyExpenses(Now);

        var posted = Assert.Single(harness.Posted);
        Assert.Equal(new DateTime(2026, 9, 5), ClinicClock.ToClinicLocal(posted.ExpenseDate).Date);
        Assert.Equal("2026-09", series.LastPostedMonth);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-2] A clinic PC switched off for a quarter comes back owing three loyers, each dated in its own month.
    [Fact]
    public async Task A_Quarter_Long_Gap_Posts_One_Row_Per_Month()
    {
        var harness = new Harness(Series(ClinicA, lastPosted: "2026-06"));

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Equal(
            new[] { "2026-07", "2026-08", "2026-09" },
            harness.Posted.Select(e => MonthlyExpenseSchedule.MonthOf(e.ExpenseDate)));
    }

    // [AC-2] The second run of the same day, and every run after it: the marker has advanced, so nothing is owed.
    [Fact]
    public async Task A_Series_Already_Up_To_Date_Posts_Nothing_And_Broadcasts_Nothing()
    {
        var harness = new Harness(Series(ClinicA, lastPosted: "2026-09"));

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Empty(harness.Posted);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // [AC-7] The 31st through the pass rather than through the calculator alone — February to September 2026,
    // so each of the three month lengths is exercised on a real posting.
    [Fact]
    public async Task A_Series_On_The_Thirty_First_Posts_On_A_Shorter_Months_Last_Day()
    {
        var harness = new Harness(Series(ClinicA, lastPosted: "2026-01", dayOfMonth: 31));

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Equal(
            new[] { "2026-02-28", "2026-03-31", "2026-04-30", "2026-05-31",
                    "2026-06-30", "2026-07-31", "2026-08-31", "2026-09-30" },
            harness.Posted.Select(e => ClinicClock.ToClinicLocal(e.ExpenseDate).ToString("yyyy-MM-dd")));
    }

    // [AC-3] A posted row is an ordinary dépense carrying the series' CURRENT values, plus the back-link that
    // makes it « mensuelle » on screen. Without the link the row is indistinguishable from a colleague's entry.
    [Fact]
    public async Task A_Posted_Row_Carries_The_Series_Values_And_Names_Its_Origin()
    {
        var series = Series(ClinicA, lastPosted: "2026-08", amount: 850m);
        var harness = new Harness(series);

        await harness.Job().PostDueMonthlyExpenses(Now);

        var posted = Assert.Single(harness.Posted);
        Assert.Equal(ClinicA, posted.ClinicId);
        Assert.Equal("Loyer", posted.Category);
        Assert.Equal(850m, posted.Amount);
        Assert.Equal(PaymentMethod.Cash, posted.Method);
        Assert.Equal("Local", posted.Description);
        Assert.Equal(series.Id, posted.RecurringExpenseId);
    }

    // [AC-6] « Arrêter » means stop: the read excludes it, so a stopped series is never asked to catch up.
    [Fact]
    public async Task A_Stopped_Series_Is_Never_Posted()
    {
        var stopped = Series(ClinicA, lastPosted: "2026-06");
        stopped.Stop(Now);
        // The production read filters on CancelledAt in SQL; handed one anyway, the pass must still not post it.
        var harness = new Harness(stopped);

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Empty(harness.Posted);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ⚠️ ONE save per clinic, and it is the money-integrity assertion of the whole pass: split, a failure between
    // the rows and the markers either posts a month twice on the next run or loses it for ever.
    [Fact]
    public async Task Each_Clinic_Commits_Its_Rows_And_Its_Markers_In_One_Save()
    {
        var harness = new Harness(
            Series(ClinicA, lastPosted: "2026-07"),
            Series(ClinicB, lastPosted: "2026-07"));

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Equal(4, harness.Posted.Count);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // One clinic's failure must not cost the others theirs — the pass is per-clinic independent.
    [Fact]
    public async Task One_Clinics_Failure_Does_Not_Stop_The_Others()
    {
        var harness = new Harness(
            Series(ClinicA, lastPosted: "2026-08"),
            Series(ClinicB, lastPosted: "2026-08"));
        harness.Expenses
            .Setup(r => r.AddAsync(It.Is<Expense>(e => e.ClinicId == ClinicA), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("clinic A is broken"));

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Equal(ClinicB, Assert.Single(harness.Posted).ClinicId);
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicB, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [US-2][R-1] Both clinic-owned tables are filtered, and this pass covers every clinic. Without the
    // declaration it reads no series anywhere and logs a clean run.
    [Fact]
    public async Task The_Pass_Declares_A_Cross_Clinic_Scope()
    {
        var harness = new Harness();

        await harness.Job().PostDueMonthlyExpenses(Now);

        Assert.Equal(TenantScopeKind.SystemWide, harness.TenantScope.Kind);
        Assert.False(string.IsNullOrWhiteSpace(harness.TenantScope.SystemWideReason));
    }

    // [I6] …and names itself, or every row it writes reads « Tâche automatique » with no clue which pass wrote it.
    [Fact]
    public async Task The_Pass_Names_Itself_As_The_Audit_Actor()
    {
        var harness = new Harness();

        await harness.Job().PostDueMonthlyExpenses(Now);

        harness.AuditActor.Verify(a => a.RunAs(nameof(MonthlyExpenseJob)), Times.Once);
    }

    // [AC-8] The broadcast carries the key the expense COMMANDS emit, asked of the production resolver. A wrong
    // key is a signal nobody listens for, which on screen is indistinguishable from the pass not running.
    [Fact]
    public async Task The_Broadcast_Carries_The_Key_The_Expense_Commands_Emit()
    {
        var harness = new Harness(Series(ClinicA, lastPosted: "2026-08"));
        var expected = RealtimeResourceResolver.Resolve(typeof(CreateExpenseCommand));

        await harness.Job().PostDueMonthlyExpenses(Now);

        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicA, expected!, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

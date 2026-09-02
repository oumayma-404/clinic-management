using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Expenses;

/// <summary>
/// The three commands behind « Répéter chaque mois », « Modifier » and « Arrêter »
/// (`caisse-monthly-expenses`).
///
/// <para>The month arithmetic lives in <c>MonthlyExpenseScheduleTests</c> and the entity's rules in
/// <c>RecurringExpenseTests</c>; what these add is the composition — that ticking the switch derives the series'
/// day and month from the dépense being recorded rather than from the clock, that both rows commit in one save,
/// and that an edit never moves the marker.</para>
/// </summary>
public class MonthlyExpenseCommandTests
{
    private static readonly Guid CallerClinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IExpenseRepository> _expenses = new();
    private readonly Mock<IRecurringExpenseRepository> _series = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private readonly List<RecurringExpense> _created = new();
    private readonly List<Expense> _recorded = new();

    public MonthlyExpenseCommandTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(CallerClinic));
        _series
            .Setup(r => r.AddAsync(It.IsAny<RecurringExpense>(), It.IsAny<CancellationToken>()))
            .Callback<RecurringExpense, CancellationToken>((s, _) => _created.Add(s))
            .ReturnsAsync((RecurringExpense s, CancellationToken _) => s);
        _expenses
            .Setup(r => r.AddAsync(It.IsAny<Expense>(), It.IsAny<CancellationToken>()))
            .Callback<Expense, CancellationToken>((e, _) => _recorded.Add(e))
            .ReturnsAsync((Expense e, CancellationToken _) => e);
    }

    private CreateExpenseCommandHandler CreateHandler() =>
        new(_expenses.Object, _series.Object, _clinicResolver.Object, _uow.Object);

    private static CreateExpenseCommand Loyer(bool repeatMonthly, string day = "2026-09-05") => new()
    {
        // The bare day the form sends — an `Unspecified` kind, already a clinic-local date.
        ExpenseDate = DateTime.Parse(day),
        Category = "Loyer",
        Amount = 800m,
        Method = nameof(PaymentMethod.Cash),
        Description = "Local",
        RepeatMonthly = repeatMonthly,
    };

    // ---- Creating a series with the dépense ----

    // The switch left alone is the ordinary case, and it must cost nothing at all.
    [Fact]
    public async Task A_Depense_Recorded_Without_The_Switch_Creates_No_Series()
    {
        var result = await CreateHandler().Handle(Loyer(repeatMonthly: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_created);
        Assert.Null(Assert.Single(_recorded).RecurringExpenseId);
    }

    // [AC-1] The dépense being recorded IS the first occurrence, so its day becomes the series' day of the month
    // and its month the marker — which is what stops the pass posting the month the user has just typed twice.
    [Fact]
    public async Task Ticking_The_Switch_Derives_The_Day_And_The_Month_From_The_Typed_Date()
    {
        var result = await CreateHandler().Handle(Loyer(repeatMonthly: true, day: "2026-09-05"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var series = Assert.Single(_created);
        Assert.Equal(5, series.DayOfMonth);
        Assert.Equal("2026-09", series.LastPostedMonth);
        Assert.Equal(CallerClinic, series.ClinicId);
        Assert.True(series.IsActive);
    }

    // [AC-1] One save, so a series can never exist without the dépense that started it, or the reverse.
    [Fact]
    public async Task The_Depense_And_Its_Series_Commit_Together()
    {
        await CreateHandler().Handle(Loyer(repeatMonthly: true), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(Assert.Single(_created).Id, Assert.Single(_recorded).RecurringExpenseId);
    }

    // A dépense dated in a past month starts the series there, so the pass immediately owes the months since.
    [Fact]
    public async Task A_Backdated_Depense_Starts_Its_Series_In_Its_Own_Month()
    {
        await CreateHandler().Handle(Loyer(repeatMonthly: true, day: "2026-06-01"), CancellationToken.None);

        Assert.Equal("2026-06", Assert.Single(_created).LastPostedMonth);
    }

    // A refused dépense must not leave a series behind — the guards run before either row is built.
    [Fact]
    public async Task A_Refused_Depense_Creates_No_Series()
    {
        var command = Loyer(repeatMonthly: true);
        command.Amount = 0m;

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_created);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Modifying a series ----

    private RecurringExpense Existing(string lastPosted = "2026-09")
    {
        var series = new RecurringExpense(
            Guid.NewGuid(), CallerClinic, "Loyer", 800m, PaymentMethod.Cash, 2, lastPosted, "Local");
        _series.Setup(r => r.GetByIdAsync(series.Id, It.IsAny<CancellationToken>())).ReturnsAsync(series);
        return series;
    }

    private UpdateRecurringExpenseCommandHandler UpdateHandler() =>
        new(_series.Object, _clinicResolver.Object, _uow.Object);

    [Fact]
    public async Task Modifying_A_Series_Changes_Future_Months_Only() // [AC-5]
    {
        var series = Existing(lastPosted: "2026-09");

        var result = await UpdateHandler().Handle(
            new UpdateRecurringExpenseCommand
            {
                Id = series.Id, Category = "Loyer", Amount = 850m,
                Method = nameof(PaymentMethod.Transfer), DayOfMonth = 5, Version = 7,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(850m, series.Amount);
        Assert.Equal("2026-09", series.LastPostedMonth);
        _uow.Verify(u => u.SetExpectedVersion(series, 7u), Times.Once);
    }

    [Theory]
    [InlineData(0, "Loyer", 32)]
    [InlineData(800, "", 5)]
    [InlineData(800, "Loyer", 0)]
    public async Task A_Series_Refuses_A_Field_The_Column_Cannot_Hold(decimal amount, string category, int dayOfMonth)
    {
        var series = Existing();

        var result = await UpdateHandler().Handle(
            new UpdateRecurringExpenseCommand
            {
                Id = series.Id, Category = category, Amount = amount,
                Method = nameof(PaymentMethod.Cash), DayOfMonth = dayOfMonth,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A stopped series is « introuvable » to the edit form too: it is off the list, and re-pricing a commitment
    // the practice has ended is not an edit anybody asked for.
    [Fact]
    public async Task A_Stopped_Series_Cannot_Be_Modified()
    {
        var series = Existing();
        series.Stop(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await UpdateHandler().Handle(
            new UpdateRecurringExpenseCommand
            {
                Id = series.Id, Category = "Loyer", Amount = 850m,
                Method = nameof(PaymentMethod.Cash), DayOfMonth = 5,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UpdateRecurringExpenseCommand.NotFoundCode, result.Code);
    }

    // ---- Stopping a series ----

    private StopRecurringExpenseCommandHandler StopHandler() =>
        new(_series.Object, _clinicResolver.Object, _uow.Object);

    [Fact]
    public async Task Stopping_A_Series_Ends_It() // [AC-6]
    {
        var series = Existing();

        var result = await StopHandler().Handle(
            new StopRecurringExpenseCommand { Id = series.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(series.IsActive);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-6] Idempotent: a double tap, or a second tab, is not an error — the outcome asked for already holds.
    [Fact]
    public async Task Stopping_A_Stopped_Series_Succeeds_And_Saves_Nothing()
    {
        var series = Existing();
        series.Stop(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await StopHandler().Handle(
            new StopRecurringExpenseCommand { Id = series.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-6] No motif anywhere in the flow — the command carries the id and nothing else, so no screen can grow
    // a « pourquoi ? » field for the most self-evident event in the feature.
    [Fact]
    public void Stopping_A_Series_Asks_For_No_Reason()
    {
        Assert.Equal(
            new[] { nameof(StopRecurringExpenseCommand.Id) },
            typeof(StopRecurringExpenseCommand).GetProperties().Select(p => p.Name));
    }
}

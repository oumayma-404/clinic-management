using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// « Droit à congé » (`conges-secretaires`, droit-conges-blueprint): every balance, « solde après » and relevé is this fold.
/// Fixed dates only — the fold takes its day as a parameter, and a test reading the clock would agree with a clock-bound
/// fold by construction. The load-bearing case is <see cref="The_Relevés_Last_Balance_Is_The_Years_Remaining"/>.
/// </summary>
public class StaffLeaveLedgerTests
{
    private static readonly DateTime Recorded = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static StaffLeaveLedgerEntry Rule(decimal days, StaffLeaveAccrual accrual, DateOnly since, bool carryOver = false, bool cancelled = false) =>
        new(Guid.NewGuid(), StaffLeaveAllowanceKind.Rule, days, accrual, carryOver, since, Recorded.AddMinutes(++_seq), cancelled);

    private static StaffLeaveLedgerEntry Opening(decimal days, DateOnly on, bool cancelled = false) =>
        new(Guid.NewGuid(), StaffLeaveAllowanceKind.Opening, days, null, false, on, Recorded.AddMinutes(++_seq), cancelled);

    private static StaffLeaveLedgerEntry Adjust(decimal days, DateOnly on, bool cancelled = false) =>
        new(Guid.NewGuid(), StaffLeaveAllowanceKind.Adjustment, days, null, false, on, Recorded.AddMinutes(++_seq), cancelled);

    private static StaffLeaveUse Took(DateOnly day, decimal days) => new(Guid.NewGuid(), day, days, true);
    private static StaffLeaveUse Waits(DateOnly day, decimal days) => new(Guid.NewGuid(), day, days, false);

    private static StaffLeaveAccount Fold(DateOnly asOf, IEnumerable<StaffLeaveUse>? uses = null, params StaffLeaveLedgerEntry[] entries) =>
        StaffLeaveLedger.Fold(entries, uses ?? Array.Empty<StaffLeaveUse>(), asOf);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ---- rules ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_Yearly_Rule_Credits_Its_Days_On_1_January()
    {
        var year = Fold(D(2026, 3, 10), null, Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1))).Year(2026);

        Assert.True(year.HasAccount);
        Assert.Equal(20m, year.Credited);
        Assert.Equal(0m, year.Upcoming);
        Assert.Equal(20m, year.Remaining);
    }

    [Fact]
    public void A_Monthly_Rule_Credits_On_The_1st_And_What_Is_Still_To_Come_Is_Not_In_The_Balance()
    {
        var account = Fold(D(2026, 3, 10), null, Rule(2, StaffLeaveAccrual.Month, D(2026, 1, 1)));
        var year = account.Year(2026);

        Assert.Equal(6m, year.Credited);
        Assert.Equal(18m, year.Upcoming);
        Assert.Equal(6m, year.Remaining);
        Assert.Equal(new StaffLeaveCredit(D(2026, 4, 1), 2m), account.NextCredit);
    }

    [Fact]
    public void A_Later_Rule_Applies_From_Its_Own_Day_Only()
    {
        var year = Fold(D(2026, 12, 15), null,
            Rule(1, StaffLeaveAccrual.Month, D(2026, 1, 1)),
            Rule(2, StaffLeaveAccrual.Month, D(2026, 7, 1))).Year(2026);

        Assert.Equal(6m + 12m, year.Credited);
    }

    [Fact]
    public void A_Cancelled_Rule_Credits_Nothing()
    {
        var year = Fold(D(2026, 3, 10), null, Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1), cancelled: true)).Year(2026);

        Assert.False(year.HasAccount);
        Assert.Null(year.Remaining);
    }

    [Fact]
    public void With_Nothing_Set_There_Is_No_Balance_But_Taken_Days_Still_Count_Overall()
    {
        var account = Fold(D(2026, 3, 10), new[] { Took(D(2026, 2, 2), 3) });

        Assert.Null(account.Year(2026).Remaining);
        Assert.Empty(account.Statement(2026));
        Assert.Equal(3m, account.TakenTotal);
    }

    // ---- the opening ---------------------------------------------------------------------------------------

    [Fact]
    public void An_Opening_Absorbs_What_Came_Before_It_And_Next_Year_The_Rule_Takes_Over()
    {
        // The owner's case: 20 j par an « perdus » from 2027, she has 10 left on 6 November; a congé from before is inside the 10.
        var rule = Rule(20, StaffLeaveAccrual.Year, D(2027, 1, 1));
        var opening = Opening(10, D(2026, 11, 6));
        var march = Took(D(2026, 3, 2), 10);
        var uses = new[] { march, Took(D(2026, 12, 21), 4) };

        var account = StaffLeaveLedger.Fold(new[] { rule, opening }, uses, D(2026, 11, 6));
        var y2026 = account.Year(2026);

        Assert.Equal(10m, y2026.Opening);
        Assert.Equal(10m, y2026.Taken);
        Assert.Equal(10m, y2026.TakenInOpening);
        Assert.Equal(4m, y2026.Planned);
        Assert.Equal(6m, y2026.Remaining);
        Assert.Equal(6m, y2026.LostAtYearEnd);
        Assert.Equal(new StaffLeaveCredit(D(2027, 1, 1), 20m), account.NextCredit);
        Assert.Equal(10m, account.TakenTotal);

        // March is listed greyed, without moving the balance.
        var lines = account.Statement(2026);
        var history = Assert.Single(lines, l => l.RequestId == march.RequestId);
        Assert.True(history.Absorbed);
        Assert.Equal(y2026.Remaining, lines[^1].Balance);

        // No rule is in force on 31/12/2026: the next one says « perdus », so the 6 do not travel into 2027.
        var y2027 = account.Year(2027);
        Assert.Equal(0m, y2027.CarriedIn);
        Assert.Equal(20m, y2027.Remaining);
        Assert.Equal(6m, y2027.LostLastYear);
    }

    [Fact]
    public void A_Credit_On_The_Openings_Own_Day_Is_Inside_The_Figure()
    {
        var year = Fold(D(2026, 11, 20), null,
            Rule(2, StaffLeaveAccrual.Month, D(2026, 1, 1)),
            Opening(10, D(2026, 11, 1))).Year(2026);

        Assert.Equal(10m, year.Remaining);
    }

    [Fact]
    public void The_Latest_Opening_Wins()
    {
        var year = Fold(D(2026, 11, 20), null, Opening(10, D(2026, 3, 1)), Opening(7, D(2026, 9, 1))).Year(2026);

        Assert.Equal(7m, year.Opening);
        Assert.Equal(7m, year.Remaining);
    }

    // ---- before her droit began --------------------------------------------------------------------------

    [Fact]
    public void Congés_Taken_Before_A_First_Yearly_Rule_Are_History_Not_A_Debt()
    {
        // 8 j accepted in 2026 with no droit set; « 20 j par an » from 1 January 2027.
        var account = Fold(D(2027, 1, 2), new[] { Took(D(2026, 3, 9), 8) }, Rule(20, StaffLeaveAccrual.Year, D(2027, 1, 1)));
        var y2027 = account.Year(2027);

        Assert.Equal(0m, y2027.CarriedIn);
        Assert.Equal(20m, y2027.Remaining);
        Assert.DoesNotContain(account.Statement(2027), l => l.Kind == StaffLeaveMovementKind.CarriedIn);
        Assert.Null(account.Year(2026).Remaining);
        Assert.Equal(8m, account.Year(2026).Taken);
    }

    [Fact]
    public void Congés_Taken_Before_A_First_Monthly_Rule_In_The_Same_Year_Are_Listed_But_Never_Charged()
    {
        // 8 j in March, then « 2 j par mois » from 1 December: on 10 December she has 2, not −6.
        var march = Took(D(2026, 3, 9), 8);
        var account = Fold(D(2026, 12, 10), new[] { march }, Rule(2, StaffLeaveAccrual.Month, D(2026, 12, 1)));
        var year = account.Year(2026);

        Assert.Equal(2m, year.Remaining);
        Assert.Equal(8m, year.Taken);
        Assert.Equal(8m, year.TakenInOpening);
        var lines = account.Statement(2026);
        Assert.True(Assert.Single(lines, l => l.RequestId == march.RequestId).Absorbed);
        Assert.Equal(year.Remaining, lines[^1].Balance);
    }

    // ---- a rule changing ----------------------------------------------------------------------------------

    [Fact]
    public void A_Monthly_Rule_Starting_In_A_Year_A_Yearly_Rule_Already_Credited_Earns_Nothing_Until_1_January()
    {
        // « 20 j par an » switched to « 2 j par mois » from 1 December 2026: 2026 is 20, never 22.
        var account = Fold(D(2026, 12, 15), null,
            Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)),
            Rule(2, StaffLeaveAccrual.Month, D(2026, 12, 1)));

        Assert.Equal(20m, account.Year(2026).Credited);
        Assert.Equal(20m, account.Year(2026).Remaining);
        Assert.Equal(new StaffLeaveCredit(D(2027, 1, 1), 2m), account.NextCredit);
    }

    [Fact]
    public void A_Rule_Starting_Later_Is_Shown_Beside_The_One_In_Force()
    {
        var yearly = Rule(24, StaffLeaveAccrual.Year, D(2026, 1, 1));
        var monthly = Rule(2, StaffLeaveAccrual.Month, D(2026, 12, 1));

        var account = Fold(D(2026, 11, 9), null, yearly, monthly);

        Assert.Equal(new StaffLeaveRuleView(24, StaffLeaveAccrual.Year, false, D(2026, 1, 1)), account.Rule);
        Assert.Equal(new StaffLeaveRuleView(2, StaffLeaveAccrual.Month, false, D(2026, 12, 1)), account.NextRule);
    }

    [Fact]
    public void A_Future_Only_Rule_Is_The_Rule_And_Not_Its_Own_Next_One()
    {
        var account = Fold(D(2026, 11, 9), null, Rule(20, StaffLeaveAccrual.Year, D(2027, 1, 1)));

        Assert.Equal(D(2027, 1, 1), account.Rule!.Since);
        Assert.Null(account.NextRule);
    }

    // ---- adjustments, uses, waiting ------------------------------------------------------------------------

    [Fact]
    public void Adjustments_Move_The_Balance_And_A_Cancelled_One_Moves_Nothing()
    {
        var year = Fold(D(2026, 6, 1), null,
            Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)),
            Adjust(2.5m, D(2026, 3, 1)),
            Adjust(-1, D(2026, 4, 1)),
            Adjust(5, D(2026, 5, 1), cancelled: true)).Year(2026);

        Assert.Equal(1.5m, year.Adjusted);
        Assert.Equal(21.5m, year.Remaining);
    }

    [Fact]
    public void Taken_Planned_And_Waiting_Are_Three_Different_Numbers()
    {
        var year = Fold(D(2026, 6, 1),
            new[] { Took(D(2026, 2, 2), 3), Took(D(2026, 8, 3), 5), Waits(D(2026, 9, 7), 2) },
            Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1))).Year(2026);

        Assert.Equal(3m, year.Taken);
        Assert.Equal(5m, year.Planned);
        Assert.Equal(2m, year.Pending);
        Assert.Equal(12m, year.Remaining);
    }

    // ---- 31 December ---------------------------------------------------------------------------------------

    [Fact]
    public void Days_Not_Taken_Are_Lost_On_31_December_Under_A_Rule_That_Does_Not_Carry()
    {
        var account = Fold(D(2027, 2, 1), new[] { Took(D(2026, 5, 4), 12) }, Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)));

        Assert.Equal(8m, account.Year(2026).Expired);
        Assert.Equal(0m, account.Year(2026).Remaining);
        Assert.Equal(0m, account.Year(2027).CarriedIn);
        Assert.Equal(20m, account.Year(2027).Remaining);
    }

    [Fact]
    public void A_Rule_That_Carries_Over_Keeps_Them()
    {
        var account = Fold(D(2027, 2, 1), new[] { Took(D(2026, 5, 4), 12) },
            Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1), carryOver: true));

        Assert.Equal(0m, account.Year(2026).Expired);
        Assert.Equal(8m, account.Year(2027).CarriedIn);
        Assert.Equal(28m, account.Year(2027).Remaining);
    }

    [Fact]
    public void A_Debt_Is_Never_Wiped_By_The_Calendar()
    {
        var account = Fold(D(2027, 2, 1), new[] { Took(D(2026, 5, 4), 23) }, Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)));

        Assert.Equal(0m, account.Year(2026).Expired);
        Assert.Equal(-3m, account.Year(2027).CarriedIn);
        Assert.Equal(17m, account.Year(2027).Remaining);
    }

    [Fact]
    public void This_Years_31_December_Has_Not_Happened_Yet()
    {
        var year = Fold(D(2026, 12, 31), null, Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1))).Year(2026);

        Assert.Equal(0m, year.Expired);
        Assert.Equal(20m, year.Remaining);
        Assert.Equal(20m, year.LostAtYearEnd);
    }

    [Fact]
    public void What_Will_Be_Lost_Is_Said_Before_31_December_And_What_Was_Lost_After_It()
    {
        var entries = new[] { Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)) };
        var uses = new[] { Took(D(2026, 5, 4), 12) };

        var november = StaffLeaveLedger.Fold(entries, uses, D(2026, 11, 1));
        var february = StaffLeaveLedger.Fold(entries, uses, D(2027, 2, 1));

        Assert.Equal(8m, november.Year(2026).LostAtYearEnd);
        Assert.Null(february.Year(2026).LostAtYearEnd);
        Assert.Equal(8m, february.Year(2026).Expired);
        Assert.Equal(8m, february.Year(2027).LostLastYear);
        var first = february.Statement(2027)[0];
        Assert.Equal(StaffLeaveMovementKind.LostLastYear, first.Kind);
        Assert.Equal(-8m, first.Days);
        Assert.Equal(february.Year(2027).Remaining, february.Statement(2027)[^1].Balance);
    }

    [Fact]
    public void Nothing_Is_Announced_As_Lost_Under_A_Rule_That_Carries_Over()
    {
        var account = Fold(D(2027, 2, 1), new[] { Took(D(2026, 5, 4), 12) },
            Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1), carryOver: true));

        Assert.Null(account.Year(2026).LostAtYearEnd);
        Assert.Null(account.Year(2027).LostLastYear);
        Assert.DoesNotContain(account.Statement(2027), l => l.Kind == StaffLeaveMovementKind.LostLastYear);
    }

    // ---- the relevé ----------------------------------------------------------------------------------------

    [Fact]
    public void The_Relevés_Last_Balance_Is_The_Years_Remaining()
    {
        var account = Fold(D(2026, 11, 20),
            new[] { Took(D(2026, 2, 2), 3), Took(D(2026, 12, 21), 4), Took(D(2026, 11, 2), 1.5m) },
            Rule(2, StaffLeaveAccrual.Month, D(2026, 1, 1)),
            Adjust(1, D(2026, 6, 15)),
            Adjust(9, D(2026, 7, 1), cancelled: true));

        var lines = account.Statement(2026);

        Assert.Equal(account.Year(2026).Remaining, lines[^1].Balance);
        Assert.Contains(lines, l => l.Kind == StaffLeaveMovementKind.Planned && l.Days == -4);
        Assert.Contains(lines, l => l.Cancelled && l.Days == 9);
        Assert.DoesNotContain(lines, l => l.Kind == StaffLeaveMovementKind.Credit && l.Day > D(2026, 11, 20));
    }

    [Fact]
    public void A_Past_Years_Relevé_Ends_With_What_Was_Lost()
    {
        var lines = Fold(D(2027, 2, 1), new[] { Took(D(2026, 5, 4), 12) }, Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)))
            .Statement(2026);

        var last = lines[^1];
        Assert.Equal(StaffLeaveMovementKind.Expired, last.Kind);
        Assert.Equal(-8m, last.Days);
        Assert.Equal(0m, last.Balance);
    }

    [Fact]
    public void Next_Years_Relevé_Opens_With_What_Was_Carried()
    {
        var lines = Fold(D(2027, 2, 1), new[] { Took(D(2026, 5, 4), 12) },
            Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1), carryOver: true)).Statement(2027);

        Assert.Equal(StaffLeaveMovementKind.CarriedIn, lines[0].Kind);
        Assert.Equal(8m, lines[0].Days);
    }

    // ---- « solde après » -----------------------------------------------------------------------------------

    [Fact]
    public void The_Balance_After_A_Congé_Counts_What_A_Monthly_Rule_Will_Have_Credited_By_Its_First_Day()
    {
        var account = StaffLeaveLedger.Fold(
            new[] { Rule(2, StaffLeaveAccrual.Month, D(2026, 1, 1)) }, Array.Empty<StaffLeaveUse>(), D(2026, 3, 10), throughYear: 2026);

        Assert.Equal(6m - 3m, account.BalanceAfter(D(2026, 3, 20), 3)!.Balance);
        Assert.Equal(12m - 3m, account.BalanceAfter(D(2026, 6, 8), 3)!.Balance);
    }

    [Fact]
    public void Under_A_Monthly_Rule_The_Balance_After_Names_Its_Day_And_Is_True_On_It()
    {
        // 2 j par mois from 1 January, read on 10 March; a congé 6–10 July (5 j).
        var rule = Rule(2, StaffLeaveAccrual.Month, D(2026, 1, 1));
        var july = Took(D(2026, 7, 6), 5);
        var before = StaffLeaveLedger.Fold(new[] { rule }, Array.Empty<StaffLeaveUse>(), D(2026, 3, 10), throughYear: 2026);

        var after = before.BalanceAfter(D(2026, 7, 6), 5)!;

        Assert.Equal(9m, after.Balance);
        Assert.Equal(D(2026, 7, 6), after.On);
        Assert.Null(after.WithPending);
        var onThatDay = StaffLeaveLedger.Fold(new[] { rule }, new[] { july }, D(2026, 7, 6)).Year(2026).Remaining;
        Assert.Equal(onThatDay, after.Balance);
        Assert.Equal(1m, StaffLeaveLedger.Fold(new[] { rule }, new[] { july }, D(2026, 3, 10)).Year(2026).Remaining);
    }

    [Fact]
    public void A_Congé_Of_An_Earlier_Year_Is_Read_On_That_Years_31_December()
    {
        // On 5 January 2027, an absence for 28–31 December 2026 (4 j): 2026 had 5 left, 2027 is untouched.
        var account = StaffLeaveLedger.Fold(
            new[] { Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)) }, new[] { Took(D(2026, 5, 4), 15) }, D(2027, 1, 5),
            throughYear: 2026);

        var after = account.BalanceAfter(D(2026, 12, 28), 4)!;

        Assert.Equal(1m, after.Balance);
        Assert.Equal(D(2026, 12, 31), after.On);
    }

    [Fact]
    public void The_Balance_After_Also_Says_What_Her_Other_Waiting_Congés_Would_Leave()
    {
        var waiting = Waits(D(2026, 8, 3), 5);
        var account = StaffLeaveLedger.Fold(
            new[] { Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)) }, new[] { Took(D(2026, 2, 2), 5), waiting }, D(2026, 6, 1),
            throughYear: 2026);

        var asked = account.BalanceAfter(D(2026, 9, 7), 5)!;
        var itself = account.BalanceAfter(D(2026, 8, 3), 5, waiting.RequestId)!;

        Assert.Equal(10m, asked.Balance);
        Assert.Equal(5m, asked.WithPending);
        Assert.Equal(10m, itself.Balance);
        Assert.Null(itself.WithPending);
    }

    [Fact]
    public void The_Balance_After_Next_Years_Congé_Starts_From_What_31_December_Leaves()
    {
        var account = StaffLeaveLedger.Fold(
            new[] { Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1)) }, new[] { Took(D(2026, 5, 4), 5) }, D(2026, 11, 6), throughYear: 2027);

        Assert.Equal(20m - 4m, account.BalanceAfter(D(2027, 2, 8), 4)!.Balance);
    }

    [Fact]
    public void The_Balance_After_Equals_The_Remaining_Once_Accepted()
    {
        var rule = Rule(20, StaffLeaveAccrual.Year, D(2026, 1, 1));
        var before = StaffLeaveLedger.Fold(new[] { rule }, new[] { Took(D(2026, 3, 2), 2.5m) }, D(2026, 11, 6), throughYear: 2026);
        var after = StaffLeaveLedger.Fold(new[] { rule }, new[] { Took(D(2026, 3, 2), 2.5m), Took(D(2026, 12, 30), 2) }, D(2026, 11, 6));

        Assert.Equal(after.Year(2026).Remaining, before.BalanceAfter(D(2026, 12, 30), 2)!.Balance);
    }

    [Fact]
    public void The_Fold_Is_Total_And_Clock_Free()
    {
        var entries = new[] { Rule(2, StaffLeaveAccrual.Month, D(2026, 1, 1)), Opening(4, D(2026, 2, 10)) };
        var uses = new[] { Took(D(2026, 4, 6), 1) };

        var first = StaffLeaveLedger.Fold(entries, uses, D(2026, 6, 1)).Year(2026);
        var second = StaffLeaveLedger.Fold(entries.Reverse(), uses, D(2026, 6, 1)).Year(2026);

        Assert.Equal(first, second);
    }

    // ---- the entry -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(20, StaffLeaveAccrual.Year, 2026, 1, 1, null)]
    [InlineData(2, StaffLeaveAccrual.Month, 2026, 11, 1, null)]
    [InlineData(20, StaffLeaveAccrual.Year, 2026, 11, 1, "Un droit par an commence un 1er janvier.")]
    [InlineData(2, StaffLeaveAccrual.Month, 2026, 11, 6, "Un droit par mois commence le 1er d'un mois.")]
    [InlineData(32, StaffLeaveAccrual.Month, 2026, 11, 1, "Le nombre de jours se compte par demi-journée, entre 0 et 31 jours.")]
    [InlineData(2.25, StaffLeaveAccrual.Month, 2026, 11, 1, "Le nombre de jours se compte par demi-journée, entre 0 et 31 jours.")]
    public void A_Rule_Starts_On_A_1st_And_Counts_In_Half_Days(double days, StaffLeaveAccrual accrual, int y, int m, int d, string? expected)
    {
        Assert.Equal(expected, StaffLeaveAllowanceEntry.RuleRefusalFor((decimal)days, accrual, new DateOnly(y, m, d)));
    }

    [Fact]
    public void An_Adjustment_Needs_Days_And_A_Motif_And_A_Line_Is_Cancelled_Once()
    {
        Assert.NotNull(StaffLeaveAllowanceEntry.AdjustmentRefusalFor(0, "Motif"));
        Assert.NotNull(StaffLeaveAllowanceEntry.AdjustmentRefusalFor(2, " "));
        Assert.Null(StaffLeaveAllowanceEntry.AdjustmentRefusalFor(-1.5m, "Rattrapage"));

        var entry = StaffLeaveAllowanceEntry.Adjustment(
            Guid.NewGuid(), Guid.NewGuid(), "user-1", 2, D(2026, 11, 6), "Prime", "admin", "Dr Salma", Recorded);
        entry.Cancel("Erreur de saisie", "Dr Salma", Recorded.AddDays(1));

        Assert.True(entry.IsCancelled);
        Assert.True(entry.ToLedgerEntry().Cancelled);
        Assert.Throws<InvalidOperationException>(() => entry.Cancel("Encore", "Dr Salma", Recorded.AddDays(2)));
    }
}

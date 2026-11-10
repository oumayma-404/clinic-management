using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>One « Droit à congé » line as the fold reads it — the entity's projection, so the fold has one input shape.</summary>
public sealed record StaffLeaveLedgerEntry(
    Guid Id, StaffLeaveAllowanceKind Kind, decimal Days, StaffLeaveAccrual? Accrual, bool CarryOver, DateOnly OnDay,
    DateTime RecordedAtUtc, bool Cancelled);

/// <summary>
/// One congé payé's share of one year: dated on its first day, or on 1 January for the share of a congé that crosses
/// into the next year. <see cref="Accepted"/> false is a request still waiting — counted as « en attente », never taken.
/// </summary>
public sealed record StaffLeaveUse(Guid RequestId, DateOnly Day, decimal Days, bool Accepted);

public enum StaffLeaveMovementKind
{
    /// <summary>What the previous year left (or owed) on 1 January.</summary>
    CarriedIn = 1,
    Opening = 2,
    Credit = 3,
    Adjustment = 4,
    Taken = 5,

    /// <summary>An accepted congé still to come: its days are already spoken for.</summary>
    Planned = 6,

    /// <summary>Days not taken by 31 December under a rule that does not carry them over.</summary>
    Expired = 7,

    /// <summary>The previous 31 December's loss, stated at the top of the new year; it moves nothing.</summary>
    LostLastYear = 8,
}

/// <summary>
/// A line of the relevé. <see cref="Days"/> is signed (+ acquired, − taken or lost); an opening states its value.
/// <see cref="Absorbed"/>: history — before her droit began, or inside a « Solde de départ » — it does not move the balance.
/// </summary>
public sealed record StaffLeaveMovement(
    DateOnly Day, StaffLeaveMovementKind Kind, decimal Days, decimal Balance, Guid? EntryId, Guid? RequestId, bool Cancelled,
    bool Absorbed = false);

/// <summary>
/// A year as read on one day. <see cref="Remaining"/> = <see cref="Acquired"/> − (<see cref="Taken"/> −
/// <see cref="TakenInOpening"/>) − planned − expired; null with no account. <see cref="Taken"/> counts every accepted
/// day already started; <see cref="TakenInOpening"/> is the part of it that moves nothing (inside a « Solde de départ »,
/// or before her droit began). <see cref="LostAtYearEnd"/>: what 31 December will take if nothing more is taken;
/// <see cref="LostLastYear"/>: what the previous 31 December took.
/// </summary>
public sealed record StaffLeaveYear(
    int Year, bool HasAccount, decimal CarriedIn, decimal? Opening, DateOnly? OpeningDay, decimal Credited,
    decimal Adjusted, decimal Acquired, decimal Taken, decimal Planned, decimal Pending, decimal Expired,
    decimal Upcoming, decimal? Remaining, decimal TakenInOpening = 0, decimal? LostAtYearEnd = null,
    decimal? LostLastYear = null);

public sealed record StaffLeaveRuleView(decimal Days, StaffLeaveAccrual Accrual, bool CarryOver, DateOnly Since);

public sealed record StaffLeaveCredit(DateOnly Day, decimal Days);

/// <summary>
/// « Solde après »: what is left of the congé's year once it is counted, read <see cref="On"/> that day.
/// <see cref="WithPending"/>: the same if her other waiting congés payés of that year were accepted too; null with none.
/// </summary>
public sealed record StaffLeaveBalanceAfter(decimal Balance, DateOnly On, decimal? WithPending);

/// <summary>
/// Folds a secretary's « Droit à congé » lines and her congés into what she has acquired, taken and lost. Pure, total and
/// <b>clock-free</b> — <c>asOf</c> is a parameter, as in <see cref="SubscriptionLedger"/>: nothing is stored, so there is
/// no job to miss and a rule's 1st of the month « happens » on the next read.
/// <para>One running balance, walked day by day. Her account <b>begins</b> on its first line (a rule, an opening or an
/// adjustment): a congé dated before that is history, never a debt. An <b>opening</b> sets the balance (everything before
/// that day is absorbed — the figure already counts it, and a credit on the opening day is inside it too; its congés stay
/// listed as history); a <b>rule</b> credits on 1 January (par an) or on the 1st of each month (par mois), the rule in
/// force on each credit day deciding — and a year a yearly rule already credited earns no monthly credit until the next
/// 1 January; <b>adjustments</b> move it; an accepted congé takes its days on its first day; at the end of each year,
/// under a rule that does not carry over (the rule in force on 31 December, else the next one), a positive remainder
/// <b>expires</b> — a negative one carries (a debt is not wiped by a calendar).</para>
/// </summary>
public static class StaffLeaveLedger
{
    private const int OrderExpiry = -1;
    private const int OrderOpening = 0;
    private const int OrderCredit = 1;
    private const int OrderAdjustment = 2;
    private const int OrderUse = 3;

    public static StaffLeaveAccount Fold(
        IEnumerable<StaffLeaveLedgerEntry> entries, IEnumerable<StaffLeaveUse> uses, DateOnly asOf, int? throughYear = null)
    {
        var all = entries.ToList();
        var active = all.Where(e => !e.Cancelled).ToList();
        var useList = uses.ToList();
        var rules = active
            .Where(e => e is { Kind: StaffLeaveAllowanceKind.Rule, Accrual: not null })
            .OrderBy(e => e.OnDay).ThenBy(e => e.RecordedAtUtc).ThenBy(e => e.Id)
            .ToList();
        var accountDays = rules.Select(r => r.OnDay)
            .Concat(active.Where(e => e.Kind is StaffLeaveAllowanceKind.Opening or StaffLeaveAllowanceKind.Adjustment).Select(e => e.OnDay))
            .ToList();
        DateOnly? accountStart = accountDays.Count == 0 ? null : accountDays.Min();

        var years = active.Select(e => e.OnDay.Year).Concat(useList.Select(u => u.Day.Year)).Append(asOf.Year).ToList();
        var firstYear = years.Min();
        var horizon = Math.Max(years.Max(), throughYear ?? asOf.Year) + 1;

        var events = new List<Line>();
        foreach (var e in active)
        {
            if (e.Kind == StaffLeaveAllowanceKind.Opening)
                events.Add(new Line(e.OnDay, e.OnDay, OrderOpening, StaffLeaveMovementKind.Opening, e.Days, e.Id, null, e.RecordedAtUtc, false));
            else if (e.Kind == StaffLeaveAllowanceKind.Adjustment)
                events.Add(new Line(e.OnDay, e.OnDay, OrderAdjustment, StaffLeaveMovementKind.Adjustment, e.Days, e.Id, null, e.RecordedAtUtc, false));
        }

        if (rules.Count > 0)
        {
            var first = rules[0].OnDay;
            var end = new DateOnly(horizon, 12, 31);
            var creditedByYearlyRule = new HashSet<int>();
            for (var month = new DateOnly(first.Year, first.Month, 1); month <= end; month = month.AddMonths(1))
            {
                var rule = RuleOn(rules, month);
                if (rule is null || rule.Days == 0)
                    continue;
                if (rule.Accrual == StaffLeaveAccrual.Year)
                {
                    if (month.Month != 1)
                        continue;
                    creditedByYearlyRule.Add(month.Year);
                }
                else if (creditedByYearlyRule.Contains(month.Year))
                {
                    // That year's droit was already given whole on 1 January.
                    continue;
                }

                events.Add(new Line(month, month, OrderCredit, StaffLeaveMovementKind.Credit, rule.Days, rule.Id, null, rule.RecordedAtUtc, false));
            }
        }

        foreach (var use in useList.Where(u => u.Accepted))
        {
            events.Add(new Line(
                use.Day, use.Day, OrderUse, StaffLeaveMovementKind.Taken, -use.Days, null, use.RequestId, DateTime.MinValue, false,
                Absorbed: accountStart is not { } start || use.Day < start));
        }

        var cancelled = all
            .Where(e => e.Cancelled && e.Kind != StaffLeaveAllowanceKind.Rule)
            .Select(e => new Line(
                e.OnDay, e.OnDay, e.Kind == StaffLeaveAllowanceKind.Opening ? OrderOpening : OrderAdjustment,
                e.Kind == StaffLeaveAllowanceKind.Opening ? StaffLeaveMovementKind.Opening : StaffLeaveMovementKind.Adjustment,
                e.Days, e.Id, null, e.RecordedAtUtc, true))
            .ToList();

        var walked = new Dictionary<int, WalkedYear>();
        var balance = 0m;
        foreach (var year in Enumerable.Range(firstYear, horizon - firstYear + 1))
        {
            var walkedYear = new WalkedYear(year, balance);
            foreach (var line in events.Where(l => l.Day.Year == year).OrderBy(Key).ThenBy(l => l.Recorded).ThenBy(l => l.EntryId ?? l.RequestId))
            {
                if (line.Absorbed)
                {
                    walkedYear.Lines.Add(line);
                    continue;
                }

                switch (line.Kind)
                {
                    case StaffLeaveMovementKind.Opening:
                        // Everything before it this year is inside the figure; its congés stay listed as history.
                        var history = walkedYear.Lines
                            .Where(l => l.Kind == StaffLeaveMovementKind.Taken)
                            .Select(l => l with { Absorbed = true })
                            .ToList();
                        walkedYear.Lines.Clear();
                        walkedYear.Lines.AddRange(history);
                        walkedYear.OpeningDay = line.Day;
                        balance = line.Days;
                        break;
                    case StaffLeaveMovementKind.Credit when walkedYear.OpeningDay == line.Day:
                        continue;
                    default:
                        balance += line.Days;
                        break;
                }

                walkedYear.Lines.Add(line);
            }

            var yearEnd = new DateOnly(year, 12, 31);
            var ruleAtEnd = RuleOn(rules, yearEnd) ?? NextRuleAfter(rules, yearEnd);
            if (ruleAtEnd is { CarryOver: false } && balance > 0)
            {
                walkedYear.Lines.Add(new Line(
                    yearEnd, new DateOnly(year + 1, 1, 1), OrderExpiry, StaffLeaveMovementKind.Expired,
                    -balance, null, null, DateTime.MinValue, false));
                balance = 0;
            }

            walked[year] = walkedYear;
        }

        return new StaffLeaveAccount(asOf, rules, accountStart, walked, events, cancelled, useList);
    }

    internal static StaffLeaveLedgerEntry? RuleOn(IReadOnlyList<StaffLeaveLedgerEntry> rules, DateOnly day) =>
        rules.LastOrDefault(r => r.OnDay <= day);

    /// <summary>The rule that will be in force on the first rule day after <paramref name="day"/> (the last saved that day).</summary>
    internal static StaffLeaveLedgerEntry? NextRuleAfter(IReadOnlyList<StaffLeaveLedgerEntry> rules, DateOnly day) =>
        rules.FirstOrDefault(r => r.OnDay > day) is { } first ? RuleOn(rules, first.OnDay) : null;

    internal static (DateOnly, int) Key(Line line) => (line.Effective, line.Order);

    internal sealed record Line(
        DateOnly Day, DateOnly Effective, int Order, StaffLeaveMovementKind Kind, decimal Days, Guid? EntryId, Guid? RequestId,
        DateTime Recorded, bool Cancelled, bool Absorbed = false);

    internal sealed class WalkedYear(int year, decimal startBalance)
    {
        public int Year { get; } = year;

        /// <summary>The balance on 1 January, after the previous year's expiry.</summary>
        public decimal StartBalance { get; } = startBalance;

        public DateOnly? OpeningDay { get; set; }

        /// <summary>The lines of the year in order — absorbed congés included, from the latest opening on; the expiry last.</summary>
        public List<Line> Lines { get; } = new();
    }
}

/// <summary>A folded account, read on any day of any year it covers. Built only by <see cref="StaffLeaveLedger.Fold"/>.</summary>
public sealed class StaffLeaveAccount
{
    private readonly IReadOnlyList<StaffLeaveLedgerEntry> _rules;
    private readonly DateOnly? _accountStart;
    private readonly IReadOnlyDictionary<int, StaffLeaveLedger.WalkedYear> _years;
    private readonly IReadOnlyList<StaffLeaveLedger.Line> _events;
    private readonly IReadOnlyList<StaffLeaveLedger.Line> _cancelled;
    private readonly IReadOnlyList<StaffLeaveUse> _uses;

    internal StaffLeaveAccount(
        DateOnly asOf, IReadOnlyList<StaffLeaveLedgerEntry> rules, DateOnly? accountStart,
        IReadOnlyDictionary<int, StaffLeaveLedger.WalkedYear> years, IReadOnlyList<StaffLeaveLedger.Line> events,
        IReadOnlyList<StaffLeaveLedger.Line> cancelled, IReadOnlyList<StaffLeaveUse> uses)
    {
        AsOf = asOf;
        _rules = rules;
        _accountStart = accountStart;
        _years = years;
        _events = events;
        _cancelled = cancelled;
        _uses = uses;
    }

    public DateOnly AsOf { get; }

    /// <summary>Every accepted day already taken, every year, whatever an opening absorbed — « total pris depuis le début ».</summary>
    public decimal TakenTotal => _uses.Where(u => u.Accepted && u.Day <= AsOf).Sum(u => u.Days);

    /// <summary>The rule in force today, or the first one still to start.</summary>
    public StaffLeaveRuleView? Rule =>
        View(StaffLeaveLedger.RuleOn(_rules, AsOf) ?? StaffLeaveLedger.NextRuleAfter(_rules, AsOf));

    /// <summary>A rule starting after today, beside <see cref="Rule"/> (never the same one).</summary>
    public StaffLeaveRuleView? NextRule
    {
        get
        {
            var next = StaffLeaveLedger.NextRuleAfter(_rules, AsOf);
            if (next is not null && StaffLeaveLedger.RuleOn(_rules, AsOf) is null)
                next = StaffLeaveLedger.NextRuleAfter(_rules, next.OnDay);
            return View(next);
        }
    }

    /// <summary>The next credit after today, if a rule will give one.</summary>
    public StaffLeaveCredit? NextCredit =>
        _events
            .Where(l => l.Kind == StaffLeaveMovementKind.Credit && l.Effective > AsOf)
            .OrderBy(l => l.Effective)
            .Select(l => new StaffLeaveCredit(l.Day, l.Days))
            .FirstOrDefault();

    /// <summary>
    /// The year read on its own day: today for this year, 31 December (expiry included) for a past one, 1 January for
    /// a future one.
    /// </summary>
    public StaffLeaveYear Year(int year)
    {
        var on = ReadDay(year);
        if (!_years.TryGetValue(year, out var walked))
            return new StaffLeaveYear(year, false, 0, null, null, 0, 0, 0, 0, 0, 0, 0, 0, null, 0, null, LostIn(year - 1));

        var lines = walked.Lines;
        var opening = lines.FirstOrDefault(l => l.Kind == StaffLeaveMovementKind.Opening);
        var carriedIn = walked.OpeningDay is null ? walked.StartBalance : 0;
        var credited = Sum(lines, StaffLeaveMovementKind.Credit, l => l.Effective <= on);
        var upcoming = Sum(lines, StaffLeaveMovementKind.Credit, l => l.Effective > on);
        var adjusted = Sum(lines, StaffLeaveMovementKind.Adjustment, l => l.Effective <= on);
        var taken = -Sum(lines, StaffLeaveMovementKind.Taken, l => l.Effective <= on);
        var takenAbsorbed = -Sum(lines, StaffLeaveMovementKind.Taken, l => l.Effective <= on && l.Absorbed);
        var planned = -Sum(lines, StaffLeaveMovementKind.Taken, l => l.Effective > on);
        var plannedAbsorbed = -Sum(lines, StaffLeaveMovementKind.Taken, l => l.Effective > on && l.Absorbed);
        var expired = -Sum(lines, StaffLeaveMovementKind.Expired, l => l.Effective <= on);
        var expiring = -Sum(lines, StaffLeaveMovementKind.Expired, l => l.Effective > on);
        var pending = _uses
            .Where(u => !u.Accepted && u.Day.Year == year && (walked.OpeningDay is not { } d || u.Day >= d))
            .Sum(u => u.Days);
        var acquired = carriedIn + (opening?.Days ?? 0) + credited + adjusted;
        var hasAccount = HasAccountOn(AccountDay(year, on));

        return new StaffLeaveYear(
            year, hasAccount, carriedIn, opening?.Days, opening?.Day, credited, adjusted, acquired, taken, planned, pending,
            expired, upcoming,
            hasAccount ? acquired - (taken - takenAbsorbed) - (planned - plannedAbsorbed) - expired : null,
            takenAbsorbed,
            hasAccount && expiring > 0 ? expiring : null,
            LostIn(year - 1));
    }

    /// <summary>
    /// The relevé of a year: what the previous 31 December took, then the lines that count up to its reading day, then the
    /// accepted congés still to come, each with the balance after it — so the last balance is the year's
    /// <see cref="StaffLeaveYear.Remaining"/>. Absorbed congés, and cancelled openings and adjustments, are listed where
    /// they stood, moving nothing.
    /// </summary>
    public IReadOnlyList<StaffLeaveMovement> Statement(int year)
    {
        var on = ReadDay(year);
        if (!_years.TryGetValue(year, out var walked) || !HasAccountOn(AccountDay(year, on)))
            return Array.Empty<StaffLeaveMovement>();

        var shown = walked.Lines
            .Where(l => l.Effective <= on || l.Kind == StaffLeaveMovementKind.Taken)
            .Select(l => l.Effective > on && l.Kind == StaffLeaveMovementKind.Taken ? l with { Kind = StaffLeaveMovementKind.Planned } : l)
            .Concat(_cancelled.Where(l => l.Day.Year == year && l.Day <= on && (walked.OpeningDay is not { } d || l.Day >= d)))
            .OrderBy(StaffLeaveLedger.Key).ThenBy(l => l.Recorded).ThenBy(l => l.EntryId ?? l.RequestId)
            .ToList();

        var result = new List<StaffLeaveMovement>(shown.Count + 2);
        var running = walked.OpeningDay is null ? walked.StartBalance : 0m;
        if (LostIn(year - 1) is { } lost)
            result.Add(new StaffLeaveMovement(new DateOnly(year, 1, 1), StaffLeaveMovementKind.LostLastYear, -lost, running, null, null, false));
        if (walked.OpeningDay is null && walked.StartBalance != 0)
            result.Add(new StaffLeaveMovement(new DateOnly(year, 1, 1), StaffLeaveMovementKind.CarriedIn, walked.StartBalance, running, null, null, false));

        foreach (var line in shown)
        {
            if (!line.Cancelled && !line.Absorbed)
                running = line.Kind == StaffLeaveMovementKind.Opening ? line.Days : running + line.Days;
            result.Add(new StaffLeaveMovement(
                line.Day, line.Kind, line.Days, running, line.EntryId, line.RequestId, line.Cancelled, line.Absorbed));
        }

        return result;
    }

    /// <summary>
    /// What is left of <paramref name="start"/>'s year once a congé taking <paramref name="share"/> of it is counted —
    /// read on its first day (or today, if that is later), so days a monthly rule will have credited by then count; a congé
    /// that began in an earlier year is read on that year's 31 December, before its expiry. A congé the account would
    /// absorb (before her droit began, or before that year's opening) takes nothing. <paramref name="requestId"/> is the
    /// request itself, left out of her other waiting ones. Null with no account; the account must have been folded
    /// through that year.
    /// </summary>
    public StaffLeaveBalanceAfter? BalanceAfter(DateOnly start, decimal share, Guid? requestId = null)
    {
        var on = start.Year < AsOf.Year ? new DateOnly(start.Year, 12, 31) : start > AsOf ? start : AsOf;
        if (!HasAccountOn(on) || !_years.TryGetValue(start.Year, out var walked))
            return null;

        var balance = walked.OpeningDay is null ? walked.StartBalance : 0m;
        foreach (var line in walked.Lines.Where(l => !l.Absorbed))
        {
            if (line.Kind == StaffLeaveMovementKind.Opening)
            {
                if (line.Effective <= on)
                    balance = line.Days;
            }
            else if (line.Kind == StaffLeaveMovementKind.Taken || (line.Effective <= on && line.Kind != StaffLeaveMovementKind.Expired))
            {
                balance += line.Days;
            }
        }

        var after = balance - (IsAbsorbed(walked, start) ? 0 : share);
        var othersWaiting = _uses
            .Where(u => !u.Accepted && u.Day.Year == start.Year && u.RequestId != requestId && !IsAbsorbed(walked, u.Day))
            .ToList();
        return new StaffLeaveBalanceAfter(after, on, othersWaiting.Count == 0 ? null : after - othersWaiting.Sum(u => u.Days));
    }

    private static decimal Sum(
        IEnumerable<StaffLeaveLedger.Line> lines, StaffLeaveMovementKind kind, Func<StaffLeaveLedger.Line, bool> where) =>
        lines.Where(l => l.Kind == kind && where(l)).Sum(l => l.Days);

    private static StaffLeaveRuleView? View(StaffLeaveLedgerEntry? rule) =>
        rule is null ? null : new StaffLeaveRuleView(rule.Days, rule.Accrual!.Value, rule.CarryOver, rule.OnDay);

    /// <summary>What 31 December of <paramref name="year"/> took; null when nothing.</summary>
    private decimal? LostIn(int year)
    {
        if (!_years.TryGetValue(year, out var walked))
            return null;
        var lost = -Sum(walked.Lines, StaffLeaveMovementKind.Expired, _ => true);
        return lost > 0 ? lost : null;
    }

    private bool IsAbsorbed(StaffLeaveLedger.WalkedYear walked, DateOnly day) =>
        _accountStart is not { } start || day < start || (walked.OpeningDay is { } opening && day < opening);

    private DateOnly ReadDay(int year) =>
        year == AsOf.Year ? AsOf
        : year < AsOf.Year ? new DateOnly(year + 1, 1, 1)
        : new DateOnly(year, 1, 1);

    /// <summary>A past year is read on 1 January after it (so its expiry shows), but only its own lines make an account.</summary>
    private static DateOnly AccountDay(int year, DateOnly on) => on.Year > year ? new DateOnly(year, 12, 31) : on;

    private bool HasAccountOn(DateOnly day) => _accountStart is { } start && start <= day;
}

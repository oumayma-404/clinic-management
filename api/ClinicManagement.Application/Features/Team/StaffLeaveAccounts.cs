using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// The one bridge between the stored congés and <see cref="StaffLeaveLedger"/>: every balance, « solde après » and relevé
/// in the product is read through here, so the secretary's card, the admin's file, a waiting request and the decision
/// e-mail cannot disagree.
/// </summary>
public static class StaffLeaveAccounts
{
    /// <summary>
    /// Her account read on <paramref name="asOf"/>. Only congé payé counts; an accepted one takes its days, a waiting one
    /// is « en attente ». A congé across 31 December is split as <see cref="StaffLeaveRules.ShareInYear"/> splits it.
    /// </summary>
    public static StaffLeaveAccount Fold(
        IEnumerable<StaffLeaveAllowanceEntry> entries, IEnumerable<StaffLeaveRequest> requests, string? workingHoursJson,
        DateOnly asOf, int? throughYear = null)
    {
        var uses = new List<StaffLeaveUse>();
        foreach (var request in requests.Where(r => r.Type == StaffLeaveType.PaidLeave && r.HoldsItsDays))
        {
            var accepted = request.Status == StaffLeaveStatus.Approved;
            var days = accepted ? request.CountedDays ?? request.RequestedDays : request.RequestedDays;
            if (request.StartDate.Year == request.EndDate.Year)
            {
                uses.Add(new StaffLeaveUse(request.Id, request.StartDate, days, accepted));
                continue;
            }

            var first = StaffLeaveRules.ShareInYear(request.StartDate, request.EndDate, days, request.StartDate.Year, workingHoursJson);
            uses.Add(new StaffLeaveUse(request.Id, request.StartDate, first, accepted));
            if (days - first != 0)
                uses.Add(new StaffLeaveUse(request.Id, new DateOnly(request.EndDate.Year, 1, 1), days - first, accepted));
        }

        return StaffLeaveLedger.Fold(entries.Select(e => e.ToLedgerEntry()), uses, asOf, throughYear);
    }

    public static StaffLeaveBalanceDto Balance(StaffLeaveAccount account, int year)
    {
        var y = account.Year(year);
        var next = account.NextCredit;
        return new StaffLeaveBalanceDto
        {
            Year = year,
            HasAccount = y.HasAccount,
            CarriedIn = y.CarriedIn,
            Opening = y.Opening,
            OpeningDay = y.OpeningDay,
            Credited = y.Credited,
            Adjusted = y.Adjusted,
            Acquired = y.Acquired,
            Taken = y.Taken,
            TakenInOpening = y.TakenInOpening == 0 ? null : y.TakenInOpening,
            Planned = y.Planned,
            Pending = y.Pending,
            Expired = y.Expired,
            LostAtYearEnd = y.LostAtYearEnd,
            LostLastYear = y.LostLastYear,
            Upcoming = y.Upcoming,
            Remaining = y.Remaining,
            TakenTotal = account.TakenTotal,
            Rule = RuleDto(account.Rule),
            NextRule = RuleDto(account.NextRule),
            NextCredit = next is null ? null : new StaffLeaveCreditDto { Day = next.Day, Days = next.Days },
        };
    }

    /// <summary>
    /// What is left of the start year once a congé payé of <paramref name="days"/> is counted, and the day that is read on
    /// (its first day, so a monthly rule's credits until then count). <paramref name="requestId"/> is the request itself,
    /// left out of her other waiting ones. Null when nothing was ever set for her.
    /// </summary>
    public static StaffLeaveBalanceAfter? BalanceAfter(
        IEnumerable<StaffLeaveAllowanceEntry> entries, IEnumerable<StaffLeaveRequest> history, string? workingHoursJson,
        DateOnly start, DateOnly end, decimal days, DateOnly today, Guid? requestId = null)
    {
        var account = Fold(entries, history, workingHoursJson, today, throughYear: start.Year);
        return account.BalanceAfter(start, StaffLeaveRules.ShareInYear(start, end, days, start.Year, workingHoursJson), requestId);
    }

    public static List<StaffLeaveMovementDto> Statement(
        StaffLeaveAccount account, int year, IEnumerable<StaffLeaveRequest> requests, IEnumerable<StaffLeaveAllowanceEntry> entries)
    {
        var requestsById = requests.ToDictionary(r => r.Id);
        var entriesById = entries.ToDictionary(e => e.Id);
        return account.Statement(year)
            .Select(m =>
            {
                var entry = m.EntryId is { } eid ? entriesById.GetValueOrDefault(eid) : null;
                var request = m.RequestId is { } rid ? requestsById.GetValueOrDefault(rid) : null;
                return new StaffLeaveMovementDto
                {
                    Day = m.Day,
                    Kind = m.Kind.ToString(),
                    Days = m.Days,
                    Balance = m.Balance,
                    EntryId = m.EntryId,
                    RequestId = m.RequestId,
                    StartDate = request?.StartDate,
                    EndDate = request?.EndDate,
                    Accrual = m.Kind == StaffLeaveMovementKind.Credit ? entry?.Accrual?.ToString() : null,
                    Reason = entry?.Kind == StaffLeaveAllowanceKind.Adjustment ? entry.Reason : null,
                    Cancelled = m.Cancelled,
                    CancelReason = m.Cancelled ? entry?.CancelReason : null,
                    Absorbed = m.Absorbed,
                };
            })
            .ToList();
    }

    private static StaffLeaveRuleDto? RuleDto(StaffLeaveRuleView? rule) =>
        rule is null
            ? null
            : new StaffLeaveRuleDto { Days = rule.Days, Accrual = rule.Accrual.ToString(), CarryOver = rule.CarryOver, Since = rule.Since };

    public static StaffLeaveAllowanceEntryDto ToDto(StaffLeaveAllowanceEntry entry) => new()
    {
        Id = entry.Id,
        Kind = entry.Kind.ToString(),
        Days = entry.Days,
        Accrual = entry.Accrual?.ToString(),
        CarryOver = entry.CarryOver,
        OnDay = entry.OnDay,
        Reason = entry.Reason,
        CreatedByName = entry.CreatedByName,
        CreatedAt = entry.CreatedAt,
        Cancelled = entry.IsCancelled,
        CancelledByName = entry.CancelledByName,
        CancelledAt = entry.CancelledAt,
        CancelReason = entry.CancelReason,
    };
}

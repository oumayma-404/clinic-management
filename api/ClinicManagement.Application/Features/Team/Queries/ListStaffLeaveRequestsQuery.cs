using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// The cabinet's congé requests for the admin: <c>pending</c> (to decide, oldest first, with the balance after and
/// who else is off or waiting), <c>decided</c> (the rest, latest start first) or everything.
/// </summary>
public class ListStaffLeaveRequestsQuery : IRequest<Result<StaffLeaveRequestsPageDto>>
{
    /// <summary>The clinic day judged; today when absent. Tests pin it, so a balance never depends on when the suite runs.</summary>
    public DateOnly? Today { get; init; }

    public const string PendingFilter = "pending";
    public const string DecidedFilter = "decided";
    public const string ApprovedFilter = "approved";
    public const string RejectedFilter = "rejected";
    public const string CancelledFilter = "cancelled";

    public string? Status { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class ListStaffLeaveRequestsQueryHandler
    : IRequestHandler<ListStaffLeaveRequestsQuery, Result<StaffLeaveRequestsPageDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public ListStaffLeaveRequestsQueryHandler(
        IStaffLeaveRequestRepository requests,
        IStaffLeaveAllowanceRepository allowances,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext)
    {
        _requests = requests;
        _allowances = allowances;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffLeaveRequestsPageDto>> Handle(
        ListStaffLeaveRequestsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestsPageDto>.FailureFrom(resolved);
            var admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<StaffLeaveRequestsPageDto>.Failure(StaffLeaveRules.AdminOnly);

            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveRequestsPageDto>.Failure(StaffLeaveRules.ModuleOff);

            var filter = request.Status?.Trim().ToLowerInvariant();
            (bool? PendingOnly, StaffLeaveStatus? Status)? parsed = filter switch
            {
                null or "" => (null, null),
                ListStaffLeaveRequestsQuery.PendingFilter => (true, null),
                ListStaffLeaveRequestsQuery.DecidedFilter => (false, null),
                ListStaffLeaveRequestsQuery.ApprovedFilter => (false, StaffLeaveStatus.Approved),
                ListStaffLeaveRequestsQuery.RejectedFilter => (false, StaffLeaveStatus.Rejected),
                ListStaffLeaveRequestsQuery.CancelledFilter => (false, StaffLeaveStatus.Cancelled),
                _ => null,
            };
            if (parsed is not { } chosen)
                return Result<StaffLeaveRequestsPageDto>.Failure(StaffLeaveRules.UnknownStatusFilter);
            var (pendingOnly, status) = chosen;

            var page = await _requests.ListAsync(
                admin.ClinicId, pendingOnly, status, PageRequest.From(request.Page, request.PageSize), cancellationToken);
            var roster = await StaffLeaveRules.RosterAsync(_users, admin.ClinicId, cancellationToken);
            var today = request.Today ?? StaffLeaveRules.ClinicToday();
            var historyByUser = new Dictionary<string, IReadOnlyList<StaffLeaveRequest>>();
            var waiting = page.Items.Where(l => l.Status == StaffLeaveStatus.Pending).ToList();
            // One read for every waiting request's « who else is off » and « who else asks », filtered per request below.
            var holdingAround = waiting.Count == 0
                ? []
                : await _requests.GetHoldingOverlappingAsync(
                    admin.ClinicId, waiting.Min(l => l.StartDate), waiting.Max(l => l.EndDate), cancellationToken);

            var entriesByUser = waiting.Any(l => l.Type == StaffLeaveType.PaidLeave)
                ? (await _allowances.GetForClinicAsync(admin.ClinicId, cancellationToken)).ToLookup(e => e.UserId)
                : Enumerable.Empty<StaffLeaveAllowanceEntry>().ToLookup(e => e.UserId);

            var items = new List<StaffLeaveRequestDto>();
            foreach (var leave in page.Items)
            {
                if (leave.Status != StaffLeaveStatus.Pending)
                {
                    items.Add(StaffLeaveRules.ToDto(leave, roster, today));
                    continue;
                }

                StaffLeaveBalanceAfter? balanceAfter = null;
                if (leave.Type == StaffLeaveType.PaidLeave && roster.ContainsKey(leave.UserId))
                {
                    if (!historyByUser.TryGetValue(leave.UserId, out var history))
                    {
                        history = await _requests.GetByUserAsync(admin.ClinicId, leave.UserId, cancellationToken);
                        historyByUser[leave.UserId] = history;
                    }

                    balanceAfter = StaffLeaveAccounts.BalanceAfter(
                        entriesByUser[leave.UserId], history, clinic.WorkingHoursJson, leave.StartDate, leave.EndDate,
                        leave.RequestedDays, today, leave.Id);
                }

                items.Add(StaffLeaveRules.ToDto(
                    leave, roster, today, balanceAfter,
                    othersOff: OthersOn(leave, holdingAround, StaffLeaveStatus.Approved, roster),
                    othersPending: OthersOn(leave, holdingAround, StaffLeaveStatus.Pending, roster)));
            }

            return Result<StaffLeaveRequestsPageDto>.Success(new StaffLeaveRequestsPageDto
            {
                Items = items,
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestsPageDto>.Failure("Erreur lors du chargement des demandes de congé.", ex);
        }
    }

    /// <summary>Colleagues whose request in <paramref name="status"/> touches one of these days — never herself.</summary>
    private static List<string> OthersOn(
        StaffLeaveRequest leave, IEnumerable<StaffLeaveRequest> around, StaffLeaveStatus status,
        IReadOnlyDictionary<string, User> roster) =>
        around
            .Where(o => o.Status == status && o.UserId != leave.UserId && o.StartDate <= leave.EndDate && leave.StartDate <= o.EndDate)
            .Select(o => roster.TryGetValue(o.UserId, out var u) ? StaffLeaveRules.DisplayName(u) : null)
            .OfType<string>()
            .Distinct()
            .ToList();
}

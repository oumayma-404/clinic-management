using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// « Modifier la règle »'s live result: her balance for the current year as it would read if this rule were saved —
/// the same fold, the same refusals as <see cref="SetLeaveRuleCommand"/>, and nothing written.
/// </summary>
public class PreviewLeaveRuleQuery : IRequest<Result<StaffLeaveBalanceDto>>
{
    /// <summary>The clinic day judged; today when absent. Tests pin it, so a balance never depends on when the suite runs.</summary>
    public DateOnly? Today { get; init; }

    public string UserId { get; set; } = string.Empty;
    public decimal Days { get; set; }

    /// <summary>« Year » or « Month ».</summary>
    public string? Accrual { get; set; }

    public bool CarryOver { get; set; }
    public DateOnly Since { get; set; }
}

public class PreviewLeaveRuleQueryHandler : IRequestHandler<PreviewLeaveRuleQuery, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public PreviewLeaveRuleQueryHandler(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext)
    {
        _allowances = allowances;
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(PreviewLeaveRuleQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveAllowanceWrites.ResolveAsync(_clinicContext, _users, _clinics, request.UserId, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var (admin, clinic, target) = resolved.Value;

            var today = request.Today ?? StaffLeaveRules.ClinicToday();
            if (!Enum.TryParse<StaffLeaveAccrual>(request.Accrual, ignoreCase: true, out var accrual) || !Enum.IsDefined(accrual))
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveAllowanceWrites.AccrualRequired);
            var refusal = StaffLeaveAllowanceEntry.RuleRefusalFor(request.Days, accrual, request.Since)
                          ?? StaffLeaveAllowanceWrites.DayRefusalFor(request.Since, today, pastOnly: false);
            if (refusal is not null)
                return Result<StaffLeaveBalanceDto>.Failure(refusal);

            var entries = await _allowances.GetForUserAsync(clinic.Id, target.Id, cancellationToken);
            var history = await _requests.GetByUserAsync(clinic.Id, target.Id, cancellationToken);
            // Built in memory only, recorded now so it supersedes any rule already saved for the same day.
            var hypothetical = StaffLeaveAllowanceEntry.Rule(
                Guid.NewGuid(), clinic.Id, target.Id, request.Days, accrual, request.CarryOver, request.Since,
                admin.Id, StaffLeaveRules.DisplayName(admin), DateTime.UtcNow);

            var account = StaffLeaveAccounts.Fold(entries.Append(hypothetical), history, clinic.WorkingHoursJson, today);
            return Result<StaffLeaveBalanceDto>.Success(StaffLeaveAccounts.Balance(account, today.Year));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors du calcul du droit à congé.", ex);
        }
    }
}

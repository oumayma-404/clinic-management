using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// « Fixer le solde »'s live result: her balance for the current year as it would read if this « Solde de départ » were
/// saved — the same fold (congés already accepted for later still come off it), the same refusals as
/// <see cref="SetOpeningLeaveBalanceCommand"/>, and nothing written.
/// </summary>
public class PreviewOpeningLeaveBalanceQuery : IRequest<Result<StaffLeaveBalanceDto>>
{
    /// <summary>The clinic day judged; today when absent. Tests pin it, so a balance never depends on when the suite runs.</summary>
    public DateOnly? Today { get; init; }

    public string UserId { get; set; } = string.Empty;
    public decimal Days { get; set; }
    public DateOnly OnDay { get; set; }
}

public class PreviewOpeningLeaveBalanceQueryHandler
    : IRequestHandler<PreviewOpeningLeaveBalanceQuery, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public PreviewOpeningLeaveBalanceQueryHandler(
        IStaffLeaveAllowanceRepository allowances, IStaffLeaveRequestRepository requests, IUserRepository users,
        IClinicRepository clinics, IClinicContext clinicContext)
    {
        _allowances = allowances;
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(
        PreviewOpeningLeaveBalanceQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveAllowanceWrites.ResolveAsync(_clinicContext, _users, _clinics, request.UserId, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var (admin, clinic, target) = resolved.Value;

            var today = request.Today ?? StaffLeaveRules.ClinicToday();
            var refusal = StaffLeaveAllowanceEntry.OpeningRefusalFor(request.Days)
                          ?? StaffLeaveAllowanceWrites.DayRefusalFor(request.OnDay, today, pastOnly: true);
            if (refusal is not null)
                return Result<StaffLeaveBalanceDto>.Failure(refusal);

            var entries = await _allowances.GetForUserAsync(clinic.Id, target.Id, cancellationToken);
            var history = await _requests.GetByUserAsync(clinic.Id, target.Id, cancellationToken);
            // Built in memory only, recorded now so it wins over an opening already saved for the same day.
            var hypothetical = StaffLeaveAllowanceEntry.Opening(
                Guid.NewGuid(), clinic.Id, target.Id, request.Days, request.OnDay, admin.Id,
                StaffLeaveRules.DisplayName(admin), DateTime.UtcNow);

            var account = StaffLeaveAccounts.Fold(entries.Append(hypothetical), history, clinic.WorkingHoursJson, today);
            return Result<StaffLeaveBalanceDto>.Success(StaffLeaveAccounts.Balance(account, today.Year));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors du calcul du solde de départ.", ex);
        }
    }
}

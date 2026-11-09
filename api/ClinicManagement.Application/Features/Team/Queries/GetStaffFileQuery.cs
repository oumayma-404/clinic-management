using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>A secretary's file for the admin: who she is, the year's balance, every request.</summary>
public class GetStaffFileQuery : IRequest<Result<StaffFileDto>>
{
    /// <summary>The clinic day judged; today when absent. Tests pin it, so a balance never depends on when the suite runs.</summary>
    public DateOnly? Today { get; init; }

    public string UserId { get; set; } = string.Empty;
    public int? Year { get; set; }
}

public class GetStaffFileQueryHandler : IRequestHandler<GetStaffFileQuery, Result<StaffFileDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetStaffFileQueryHandler(
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

    public async Task<Result<StaffFileDto>> Handle(GetStaffFileQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffFileDto>.FailureFrom(resolved);
            var admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<StaffFileDto>.Failure(StaffLeaveRules.AdminOnly);

            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffFileDto>.Failure(StaffLeaveRules.ModuleOff);

            var roster = await StaffLeaveRules.RosterAsync(_users, admin.ClinicId, cancellationToken);
            if (!roster.TryGetValue(request.UserId, out var target))
                return Result<StaffFileDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);

            var all = await _requests.GetByUserAsync(admin.ClinicId, target.Id, cancellationToken);
            var entries = await _allowances.GetForUserAsync(admin.ClinicId, target.Id, cancellationToken);
            var today = request.Today ?? StaffLeaveRules.ClinicToday();
            var year = request.Year ?? today.Year;
            var account = StaffLeaveAccounts.Fold(entries, all, clinic.WorkingHoursJson, today, year);

            return Result<StaffFileDto>.Success(new StaffFileDto
            {
                UserId = target.Id,
                FullName = StaffLeaveRules.DisplayName(target),
                Email = target.Email,
                Role = target.Role,
                IsActive = target.IsActive,
                Balance = StaffLeaveAccounts.Balance(account, year),
                Statement = StaffLeaveAccounts.Statement(account, year, all, entries),
                Entries = entries
                    .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                    .Select(StaffLeaveAccounts.ToDto)
                    .ToList(),
                AcceptedByType = StaffLeaveRules.AcceptedByType(year, all, clinic.WorkingHoursJson),
                Items = all.Select(r => StaffLeaveRules.ToDto(r, roster, today)).ToList(),
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffFileDto>.Failure("Erreur lors du chargement de la fiche.", ex);
        }
    }
}

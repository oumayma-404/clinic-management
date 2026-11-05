using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>The « 1 » on « Utilisateurs »: requests waiting for an admin. Zero while congés are switched off.</summary>
public class GetPendingStaffLeaveCountQuery : IRequest<Result<PendingStaffLeaveCountDto>>
{
}

public class GetPendingStaffLeaveCountQueryHandler
    : IRequestHandler<GetPendingStaffLeaveCountQuery, Result<PendingStaffLeaveCountDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetPendingStaffLeaveCountQueryHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext)
    {
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<PendingStaffLeaveCountDto>> Handle(
        GetPendingStaffLeaveCountQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<PendingStaffLeaveCountDto>.FailureFrom(resolved);
            var admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<PendingStaffLeaveCountDto>.Failure(StaffLeaveRules.AdminOnly);

            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            var count = clinic is { LeaveRequestsEnabled: true }
                ? await _requests.CountPendingAsync(admin.ClinicId, cancellationToken)
                : 0;
            return Result<PendingStaffLeaveCountDto>.Success(new PendingStaffLeaveCountDto { Count = count });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PendingStaffLeaveCountDto>.Failure("Erreur lors du comptage des demandes de congé.", ex);
        }
    }
}

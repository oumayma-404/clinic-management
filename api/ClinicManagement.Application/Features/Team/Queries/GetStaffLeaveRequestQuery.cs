using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>One request as it is now — what a decision or a withdrawal re-reads before it writes. Its owner or an admin.</summary>
public class GetStaffLeaveRequestQuery : IRequest<Result<StaffLeaveRequestDto>>
{
    public Guid Id { get; set; }
}

public class GetStaffLeaveRequestQueryHandler : IRequestHandler<GetStaffLeaveRequestQuery, Result<StaffLeaveRequestDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;

    public GetStaffLeaveRequestQueryHandler(
        IStaffLeaveRequestRepository requests, IUserRepository users, IClinicContext clinicContext)
    {
        _requests = requests;
        _users = users;
        _clinicContext = clinicContext;
    }

    public async Task<Result<StaffLeaveRequestDto>> Handle(GetStaffLeaveRequestQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(resolved);
            var caller = resolved.Value!;

            var lookup = await StaffLeaveRules.ResolveRequestAsync(_requests, caller.ClinicId, request.Id, cancellationToken);
            if (lookup.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(lookup);
            var leave = lookup.Value!;
            // Another secretary's request reads as not found, never as « not yours ».
            if (leave.UserId != caller.Id && !caller.IsAdmin())
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.RequestNotFound, StaffLeaveRules.NotFoundCode);

            var roster = await StaffLeaveRules.RosterAsync(_users, caller.ClinicId, cancellationToken);
            return Result<StaffLeaveRequestDto>.Success(
                StaffLeaveRules.ToDto(leave, roster, StaffLeaveRules.ClinicToday(), viewerIsAdmin: caller.IsAdmin()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestDto>.Failure("Erreur lors du chargement de la demande de congé.", ex);
        }
    }
}

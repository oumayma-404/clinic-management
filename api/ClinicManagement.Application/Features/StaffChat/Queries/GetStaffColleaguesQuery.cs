using System.Globalization;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.StaffChat.Queries;

/// <summary>The active colleagues the caller can write to, A→Z — open to every role, unlike the admin's user list.</summary>
public class GetStaffColleaguesQuery : IRequest<Result<List<StaffColleagueDto>>>
{
}

public class GetStaffColleaguesQueryHandler : IRequestHandler<GetStaffColleaguesQuery, Result<List<StaffColleagueDto>>>
{
    private static readonly StringComparer FrenchOrder = StringComparer.Create(new CultureInfo("fr-FR"), ignoreCase: true);

    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IStaffPresence _presence;

    public GetStaffColleaguesQueryHandler(
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IStaffPresence presence)
    {
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _presence = presence;
    }

    public async Task<Result<List<StaffColleagueDto>>> Handle(GetStaffColleaguesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<List<StaffColleagueDto>>.FailureFrom(loaded);
            }

            var roster = loaded.Value!;
            var colleagues = roster.Colleagues
                .Where(c => c.IsActive && c.UserId != roster.Viewer.UserId)
                .OrderBy(c => c.Name, FrenchOrder)
                .ThenBy(c => c.UserId, StringComparer.Ordinal)
                .Select(c => StaffChatInbox.ToColleagueDto(c, roster, _presence))
                .ToList();

            return Result<List<StaffColleagueDto>>.Success(colleagues);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<StaffColleagueDto>>.Failure("Impossible de charger les collègues.", ex);
        }
    }
}

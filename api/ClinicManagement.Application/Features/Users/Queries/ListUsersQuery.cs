using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Queries;

/// <summary>
/// Admin-only: lists the users of the caller's clinic with their account status
/// (AC-5.1, AC-5.4).
/// </summary>
public class ListUsersQuery : IRequest<Result<IEnumerable<ClinicUserDto>>>
{
}

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, Result<IEnumerable<ClinicUserDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public ListUsersQueryHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<ClinicUserDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<ClinicUserDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var currentUser = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (currentUser == null)
            {
                return Result<IEnumerable<ClinicUserDto>>.Failure("Utilisateur introuvable.");
            }

            // AC-5.4: only an admin can view the user list.
            if (!currentUser.IsAdmin())
            {
                return Result<IEnumerable<ClinicUserDto>>.Failure("Seuls les administrateurs peuvent consulter les utilisateurs.");
            }

            var users = await _userRepository.GetByClinicIdAsync(currentUser.ClinicId, cancellationToken);

            var dtos = users
                .OrderBy(u => u.FullName)
                .Select(u => new ClinicUserDto
                {
                    Id = u.Id,
                    ClinicId = u.ClinicId,
                    Role = u.Role,
                    Email = u.Email,
                    FullName = u.FullName,
                    IsActive = u.IsActive,
                    MustChangePassword = u.MustChangePassword,
                    LastLoginAt = u.LastLoginAt,
                    CreatedAt = u.CreatedAt
                });

            return Result<IEnumerable<ClinicUserDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<ClinicUserDto>>.Failure($"Error retrieving users: {ex.Message}");
        }
    }
}

using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Queries;

public class GetUsersQuery : IRequest<Result<IEnumerable<UserDto>>>
{
}

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, Result<IEnumerable<UserDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetUsersQueryHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<UserDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<UserDto>>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<UserDto>>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            var userRole = _clinicContext.GetUserRole();
            if (userRole != "admin")
            {
                return Result<IEnumerable<UserDto>>.Failure("Only admins can view users");
            }

            var users = await _userRepository.GetByClinicIdAsync(clinicId, cancellationToken);

            var dtos = users.Select(u => new UserDto
            {
                Id = u.Id,
                ClinicId = u.ClinicId,
                Role = u.Role,
                Email = u.Email,
                FullName = u.FullName,
                CreatedAt = u.CreatedAt
            });

            return Result<IEnumerable<UserDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<UserDto>>.Failure($"Error retrieving users: {ex.Message}");
        }
    }
}




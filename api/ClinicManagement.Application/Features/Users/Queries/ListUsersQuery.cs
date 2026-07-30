using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Queries;

/// <summary>
/// Admin-only: lists the users of the caller's clinic with their account status
/// (AC-5.1, AC-5.4).
/// </summary>
public class ListUsersQuery : IRequest<Result<PagedResult<ClinicUserDto>>>
{

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }
}

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, Result<PagedResult<ClinicUserDto>>>
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

    public async Task<Result<PagedResult<ClinicUserDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<PagedResult<ClinicUserDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var currentUser = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (currentUser == null)
            {
                return Result<PagedResult<ClinicUserDto>>.Failure("Utilisateur introuvable.");
            }

            // AC-5.4: only an admin can view the user list.
            if (!currentUser.IsAdmin())
            {
                return Result<PagedResult<ClinicUserDto>>.Failure("Seuls les administrateurs peuvent consulter les utilisateurs.");
            }

            // Ordering moved into the repository: a paged read has to be ordered in SQL, and the OrderBy that
            // used to be here would only have sorted the rows already inside the page.
            var page = await _userRepository.GetByClinicIdAsync(
                currentUser.ClinicId,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            var dtos = page
                .Map(u => new ClinicUserDto
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

            return Result<PagedResult<ClinicUserDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<ClinicUserDto>>.Failure($"Error retrieving users: {ex.Message}");
        }
    }
}

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
public class ListUsersQuery : IRequest<Result<ClinicUsersPageDto>>
{

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }
}

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, Result<ClinicUsersPageDto>>
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

    public async Task<Result<ClinicUsersPageDto>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<ClinicUsersPageDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var currentUser = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (currentUser == null)
            {
                return Result<ClinicUsersPageDto>.Failure("Utilisateur introuvable.");
            }

            // AC-5.4: only an admin can view the user list.
            if (!currentUser.IsAdmin())
            {
                return Result<ClinicUsersPageDto>.Failure("Seuls les administrateurs peuvent consulter les utilisateurs.");
            }

            // Ordering moved into the repository: a paged read has to be ordered in SQL, and the OrderBy that
            // used to be here would only have sorted the rows already inside the page.
            var page = await _userRepository.GetByClinicIdAsync(
                currentUser.ClinicId,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            var dtos = page
                .Map(u => u.ToClinicUserDto());

            // Counted over the whole clinic and outside the search term (I5). The figure exists to tell an admin
            // that someone cannot get in yet, so scoping it to the rows they happen to be looking at would hide
            // exactly the case it is for.
            var pendingCount = await _userRepository.CountPendingActivationAsync(
                currentUser.ClinicId,
                cancellationToken);

            return Result<ClinicUsersPageDto>.Success(new ClinicUsersPageDto
            {
                Items = dtos.Items.ToList(),
                PendingActivationCount = pendingCount,
                Page = dtos.Page,
                PageSize = dtos.PageSize,
                TotalCount = dtos.TotalCount,
                TotalPages = dtos.TotalPages,
                HasPreviousPage = dtos.HasPreviousPage,
                HasNextPage = dtos.HasNextPage,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // ⚠️ No `ex.Message`, and no English. This was the ONLY one of five sibling handlers that printed the
            // server's raw exception — the other four return a generic French sentence and log — so a 500 on the
            // users screen surfaced « Error retrieving users: 42P01: relation … does not exist » to a
            // French-speaking dentist. The details are in the log above, where they belong.
            return Result<ClinicUsersPageDto>.Failure("Erreur lors du chargement des utilisateurs.");
        }
    }
}

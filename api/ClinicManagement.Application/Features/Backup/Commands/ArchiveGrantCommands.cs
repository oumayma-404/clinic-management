using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Backup.Commands;

/// <summary>
/// The three admin operations on an archive device grant (<c>clinic-archive-auto-copy</c>), in one file because
/// they are one small CRUD surface over one table and splitting them into three would put the same fifteen lines
/// of caller resolution in three places.
///
/// <para>⚠️ Every one of them re-checks <c>IsAdmin</c> behind the controller's <c>AdminOnly</c> policy, the shape
/// every sibling in this folder uses: issuing a grant is handing a machine the cabinet's whole record.</para>
/// </summary>
internal static class ArchiveGrantGuard
{
    /// <summary>The caller's clinic, or a French refusal. One copy of what all three handlers need first.</summary>
    internal static async Task<Result<Guid>> ResolveAdminClinicAsync(
        IClinicContext clinicContext,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        var callerId = clinicContext.GetUserId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Result<Guid>.Failure("Session invalide, veuillez vous reconnecter.");
        }

        var caller = await users.GetByAuth0SubAsync(callerId, cancellationToken);
        if (caller == null)
        {
            return Result<Guid>.Failure("Utilisateur introuvable.");
        }

        if (!caller.IsAdmin())
        {
            return Result<Guid>.Failure("Seuls les administrateurs peuvent gérer les postes autorisés.");
        }

        return Result<Guid>.Success(caller.ClinicId);
    }

    /// <summary>The caller's own account id, needed to stamp who issued a grant.</summary>
    internal static async Task<string?> ResolveCallerIdAsync(
        IClinicContext clinicContext, IUserRepository users, CancellationToken cancellationToken)
    {
        var callerId = clinicContext.GetUserId();
        if (string.IsNullOrEmpty(callerId))
        {
            return null;
        }

        var caller = await users.GetByAuth0SubAsync(callerId, cancellationToken);
        return caller?.Id;
    }
}

public class IssueArchiveGrantCommand : IRequest<Result<IssuedArchiveGrantDto>>
{
    public string Label { get; set; } = string.Empty;
}

public class IssueArchiveGrantCommandHandler
    : IRequestHandler<IssueArchiveGrantCommand, Result<IssuedArchiveGrantDto>>
{
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicArchiveGrantRepository _grants;

    public IssueArchiveGrantCommandHandler(
        IUserRepository users,
        IClinicContext clinicContext,
        IClinicArchiveGrantRepository grants)
    {
        _users = users;
        _clinicContext = clinicContext;
        _grants = grants;
    }

    public async Task<Result<IssuedArchiveGrantDto>> Handle(
        IssueArchiveGrantCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await ArchiveGrantGuard.ResolveAdminClinicAsync(_clinicContext, _users, cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<IssuedArchiveGrantDto>.Failure(clinic.Error!);
            }

            var label = (request.Label ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                return Result<IssuedArchiveGrantDto>.Failure("Donnez un nom au poste, pour pouvoir le révoquer.");
            }

            var issuedBy = await ArchiveGrantGuard.ResolveCallerIdAsync(_clinicContext, _users, cancellationToken);
            if (issuedBy == null)
            {
                return Result<IssuedArchiveGrantDto>.Failure("Utilisateur introuvable.");
            }

            var (secret, hash) = ClinicArchiveGrant.NewSecret();
            var grant = ClinicArchiveGrant.Create(clinic.Value, label, hash, issuedBy, DateTime.UtcNow);

            await _grants.AddAsync(grant, cancellationToken);

            return Result<IssuedArchiveGrantDto>.Success(
                new IssuedArchiveGrantDto(grant.Id, grant.Label, secret, grant.CreatedAtUtc));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IssuedArchiveGrantDto>.Failure($"Le poste n'a pas pu être autorisé : {ex.Message}");
        }
    }
}

public class RevokeArchiveGrantCommand : IRequest<Result<bool>>
{
    public Guid GrantId { get; set; }
}

public class RevokeArchiveGrantCommandHandler : IRequestHandler<RevokeArchiveGrantCommand, Result<bool>>
{
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicArchiveGrantRepository _grants;

    public RevokeArchiveGrantCommandHandler(
        IUserRepository users,
        IClinicContext clinicContext,
        IClinicArchiveGrantRepository grants)
    {
        _users = users;
        _clinicContext = clinicContext;
        _grants = grants;
    }

    public async Task<Result<bool>> Handle(RevokeArchiveGrantCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await ArchiveGrantGuard.ResolveAdminClinicAsync(_clinicContext, _users, cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<bool>.Failure(clinic.Error!);
            }

            var grant = await _grants.GetByIdAsync(request.GrantId, cancellationToken);

            // A grant of another cabinet reads as absent rather than as forbidden: telling a caller that an id
            // they cannot see nonetheless exists is the enumeration oracle clinic-self-signup refuses.
            if (grant == null || grant.ClinicId != clinic.Value)
            {
                return Result<bool>.Failure("Poste autorisé introuvable.");
            }

            grant.Revoke(DateTime.UtcNow);
            await _grants.UpdateAsync(grant, cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure($"Le poste n'a pas pu être révoqué : {ex.Message}");
        }
    }
}

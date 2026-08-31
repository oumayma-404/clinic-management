using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Backup.Queries;

/// <summary>
/// Which machines may pull this cabinet's archive unattended (<c>clinic-archive-auto-copy</c>).
///
/// <para>Revoked grants are listed too, and deliberately: « ce poste ne peut plus » is as much a part of the
/// answer as « ce poste peut », and hiding them would make a revocation look like a deletion of history.</para>
/// </summary>
public class ListArchiveGrantsQuery : IRequest<Result<IReadOnlyList<ArchiveGrantDto>>>
{
}

public class ListArchiveGrantsQueryHandler
    : IRequestHandler<ListArchiveGrantsQuery, Result<IReadOnlyList<ArchiveGrantDto>>>
{
    private readonly IUserRepository _users;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicArchiveGrantRepository _grants;

    public ListArchiveGrantsQueryHandler(
        IUserRepository users,
        IClinicContext clinicContext,
        IClinicArchiveGrantRepository grants)
    {
        _users = users;
        _clinicContext = clinicContext;
        _grants = grants;
    }

    public async Task<Result<IReadOnlyList<ArchiveGrantDto>>> Handle(
        ListArchiveGrantsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await ArchiveGrantGuard.ResolveAdminClinicAsync(_clinicContext, _users, cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<IReadOnlyList<ArchiveGrantDto>>.Failure(clinic.Error!);
            }

            var grants = await _grants.ListAsync(clinic.Value, cancellationToken);

            return Result<IReadOnlyList<ArchiveGrantDto>>.Success(grants.Select(Map).ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IReadOnlyList<ArchiveGrantDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }

    private static ArchiveGrantDto Map(ClinicArchiveGrant grant) => new(
        grant.Id,
        grant.Label,
        grant.CreatedAtUtc,
        grant.LastUsedAtUtc,
        grant.RevokedAtUtc);
}

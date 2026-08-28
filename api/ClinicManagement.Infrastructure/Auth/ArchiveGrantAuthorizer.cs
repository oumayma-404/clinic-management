using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Infrastructure.Auth;

/// <inheritdoc cref="IArchiveGrantAuthorizer"/>
public class ArchiveGrantAuthorizer : IArchiveGrantAuthorizer
{
    private readonly IClinicArchiveGrantRepository _grants;
    private readonly IUserRepository _users;

    public ArchiveGrantAuthorizer(IClinicArchiveGrantRepository grants, IUserRepository users)
    {
        _grants = grants;
        _users = users;
    }

    public async Task<ArchiveGrantPrincipal?> AuthorizeAsync(
        string? secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var grant = await _grants.FindBySecretHashAsync(
            ClinicArchiveGrant.HashSecret(secret), cancellationToken);

        var nowUtc = DateTime.UtcNow;
        if (grant == null || !grant.IsUsable(nowUtc))
        {
            return null;
        }

        // The account behind the grant, re-checked every time rather than trusted from issue: an admin who has
        // been deactivated or demoted since must not leave a machine able to pull the cabinet's whole record.
        var issuer = await _users.GetByIdAsync(grant.CreatedByUserId, cancellationToken);
        if (issuer == null || !issuer.IsActive || !issuer.IsAdmin() || issuer.ClinicId != grant.ClinicId)
        {
            return null;
        }

        grant.MarkUsed(nowUtc);
        await _grants.UpdateAsync(grant, cancellationToken);

        return new ArchiveGrantPrincipal(grant.Id, grant.ClinicId, issuer.Id);
    }
}

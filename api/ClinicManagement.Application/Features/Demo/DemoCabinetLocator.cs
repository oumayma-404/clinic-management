using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// Which demo cabinet a visitor enters: the newest one whose dentist-admin is active. The nightly rebuild builds the
/// next cabinet with its admin switched off and switches her on last, so a half-built cabinet is never chosen.
/// </summary>
/// <remarks>Reads <c>Clinics</c> and <c>Users</c> only — the two unfiltered tables — so it answers with no tenant scope.</remarks>
public sealed class DemoCabinetLocator
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;

    public DemoCabinetLocator(IClinicRepository clinics, IUserRepository users)
    {
        _clinics = clinics;
        _users = users;
    }

    public async Task<DemoCabinet?> CurrentAsync(CancellationToken cancellationToken)
    {
        var newestFirst = (await _clinics.GetAllAsync(cancellationToken))
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id);

        foreach (var clinic in newestFirst)
        {
            var members = await _users.GetByClinicIdAsync(clinic.Id, cancellationToken: cancellationToken);
            var admin = members.Items
                .Where(IsEnterable)
                .OrderBy(u => u.CreatedAt)
                .ThenBy(u => u.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (admin is not null)
            {
                return new DemoCabinet(clinic, admin);
            }
        }

        return null;
    }

    // A forced password change would lock every visitor on « changer le mot de passe ».
    private static bool IsEnterable(User user) =>
        user.IsAdmin() && user.IsActive && user.IsLocalAccount() && !user.MustChangePassword;
}

/// <summary>The cabinet visitors enter, and the dentist they enter as.</summary>
public sealed record DemoCabinet(Clinic Clinic, User Admin);
